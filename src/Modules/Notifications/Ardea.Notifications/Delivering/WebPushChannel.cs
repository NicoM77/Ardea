using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Ardea.Notifications.Messages;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ardea.Notifications.Delivering;

internal sealed record PushDevice(Guid Id, string Endpoint, string P256dh, string Auth);

internal enum PushDeviceOutcome
{
    Sent,

    Gone,

    Rejected,

    Refused,

    Unavailable,
}

internal sealed record PushDeviceResult(Guid Id, PushDeviceOutcome Outcome, string Status);

internal sealed class PushOptions
{
    public TimeSpan Budget { get; init; } = WebPushChannel.Budget;
}

internal sealed partial class WebPushChannel(IHttpClientFactory http, VapidKeyStore vapid, PushOptions options, TimeProvider time, ILogger<WebPushChannel> logger) : INotificationChannel
{
    public const string ClientName = "notify.push";

    public static readonly TimeSpan TimeToLive = TimeSpan.FromDays(1);

    public const int RejectionsBeforeRemoval = 3;

    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private const string BlockedTarget = "resolves to no address Ardea may connect to";

    public string Name => Channels.Push;

    public async Task<SendResult> SendAsync(Outgoing message, CancellationToken cancellationToken)
    {
        if (message.PushDevices.Count == 0)
        {
            return new SendResult(SendOutcome.Permanent, "The member has no device subscribed to push.");
        }

        var payload = Encoding.UTF8.GetBytes(MessageRenderer.Push(message.Content));
        var urgency = message.Content.Level == 5 ? "high" : "normal";
        var topic = Topic(message.Content.DeliveryId);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(options.Budget);
        var devices = await Task.WhenAll(message.PushDevices.Select(device => SendAsync(device, payload, urgency, topic, budget.Token)));
        return Summarize(devices);
    }

    public async Task<PushDeviceResult> SendAsync(PushDevice device, byte[] payload, string urgency, string? topic, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(device.Endpoint, UriKind.Absolute, out var endpoint)
            || PushConfiguration.Decode(device.P256dh) is not { Length: 65 } key || PushConfiguration.Decode(device.Auth) is not { Length: 16 } auth)
        {
            return new PushDeviceResult(device.Id, PushDeviceOutcome.Gone, "invalid");
        }

        try
        {
            var keys = await vapid.GetAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(WebPushEncryption.Encrypt(payload, key, auth)) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.ContentEncoding.Add("aes128gcm");
            request.Headers.TryAddWithoutValidation("TTL", ((int)TimeToLive.TotalSeconds).ToString(CultureInfo.InvariantCulture));
            request.Headers.TryAddWithoutValidation("Urgency", urgency);
            if (topic is not null)
            {
                request.Headers.TryAddWithoutValidation("Topic", topic);
            }

            request.Headers.TryAddWithoutValidation("Authorization", Vapid.Authorization(keys, endpoint, time.GetUtcNow()));
            request.Options.Set(OutboundAddresses.AllowInternal, false);
            request.Options.Set(OutboundAddresses.Untraced, true);

            using var client = http.CreateClient(ClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var code = (int)response.StatusCode;
            var status = code.ToString(CultureInfo.InvariantCulture);
            var outcome = code switch
            {
                >= 200 and < 300 => PushDeviceOutcome.Sent,
                404 or 410 => PushDeviceOutcome.Gone,
                401 or 403 => PushDeviceOutcome.Rejected,
                408 or 429 or >= 500 => PushDeviceOutcome.Unavailable,
                _ => PushDeviceOutcome.Refused,
            };
            return new PushDeviceResult(device.Id, outcome, status);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains(BlockedTarget, StringComparison.Ordinal) || ex.InnerException?.Message.Contains(BlockedTarget, StringComparison.Ordinal) == true)
        {
            return new PushDeviceResult(device.Id, PushDeviceOutcome.Gone, "blocked");
        }
        catch (HttpRequestException)
        {
            return new PushDeviceResult(device.Id, PushDeviceOutcome.Unavailable, "unreachable");
        }
        catch (OperationCanceledException)
        {
            return new PushDeviceResult(device.Id, PushDeviceOutcome.Unavailable, "timeout");
        }
        catch (Exception ex)
        {
            LogFailed(device.Id, ex.GetType().Name);
            return new PushDeviceResult(device.Id, PushDeviceOutcome.Unavailable, "error");
        }
    }

    public static SendResult Summarize(IReadOnlyList<PushDeviceResult> devices)
    {
        var statuses = string.Join(", ", devices.Select(d => d.Status).Distinct());
        if (devices.Any(d => d.Outcome == PushDeviceOutcome.Sent))
        {
            return new SendResult(SendOutcome.Sent, Status: statuses, Devices: devices);
        }

        var error = devices.Count == 1
            ? $"The push service answered {statuses}."
            : $"No device accepted the message; the push services answered {statuses}.";
        return devices.Any(d => d.Outcome == PushDeviceOutcome.Unavailable)
            ? new SendResult(SendOutcome.Transient, error, statuses, devices)
            : new SendResult(SendOutcome.Permanent, error, statuses, devices);
    }

    public async Task RecordAsync(NotifyDbContext db, IReadOnlyList<PushDeviceResult> devices, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        foreach (var device in devices)
        {
            var row = db.PushSubscriptions.Where(s => s.Id == device.Id);
            switch (device.Outcome)
            {
                case PushDeviceOutcome.Sent:
                    await row.ExecuteUpdateAsync(
                        s => s.SetProperty(x => x.LastSuccessAt, now).SetProperty(x => x.FailedCount, 0).SetProperty(x => x.RejectedCount, 0), cancellationToken);
                    break;
                case PushDeviceOutcome.Gone:
                case PushDeviceOutcome.Rejected when await row.AnyAsync(s => s.RejectedCount >= RejectionsBeforeRemoval - 1, cancellationToken):
                    if (await row.ExecuteDeleteAsync(cancellationToken) > 0)
                    {
                        LogRemoved(device.Id, device.Status);
                    }

                    break;
                case PushDeviceOutcome.Rejected:
                    await row.ExecuteUpdateAsync(
                        s => s.SetProperty(x => x.FailedCount, x => x.FailedCount + 1).SetProperty(x => x.RejectedCount, x => x.RejectedCount + 1), cancellationToken);
                    break;
                default:
                    await row.ExecuteUpdateAsync(
                        s => s.SetProperty(x => x.FailedCount, x => x.FailedCount + 1).SetProperty(x => x.RejectedCount, 0), cancellationToken);
                    break;
            }
        }
    }

    public static string Topic(Guid deliveryId) => WebEncoders.Base64UrlEncode(deliveryId.ToByteArray());

    public static bool IsPushService(string host, IReadOnlyCollection<string> configured)
    {
        host = host.TrimEnd('.').ToLowerInvariant();
        return host is "fcm.googleapis.com" or "updates.push.services.mozilla.com" or "web.push.apple.com"
            || host.EndsWith(".push.apple.com", StringComparison.Ordinal)
            || host.EndsWith(".notify.windows.com", StringComparison.Ordinal)
            || configured.Contains(host, StringComparer.OrdinalIgnoreCase);
    }

    public static string? CheckEndpoint(string? value, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var url))
        {
            return "Enter the subscription's endpoint, an https address of at most 2000 characters.";
        }

        if (url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length > 0 || url.Fragment.Length > 0)
        {
            return "The endpoint must be an https address without credentials or fragment.";
        }

        return IsPushService(url.IdnHost, PushConfiguration.ServiceHosts(configuration))
            ? null
            : "The endpoint is not at a known push service; Ardea sends only to the browser makers' push services.";
    }

    public static string Hash(string endpoint) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));

    [LoggerMessage(EventId = 6110, Level = LogLevel.Information, Message = "Push subscription {SubscriptionId} was removed: its push service answered {Status}")]
    private partial void LogRemoved(Guid subscriptionId, string status);

    [LoggerMessage(EventId = 6111, Level = LogLevel.Warning, Message = "Sending to push subscription {SubscriptionId} failed with {Error}")]
    private partial void LogFailed(Guid subscriptionId, string error);
}
