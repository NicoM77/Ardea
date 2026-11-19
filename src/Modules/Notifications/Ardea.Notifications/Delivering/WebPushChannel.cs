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

/// <summary>A subscribed device a push message goes to; read in the first step of the delivery.</summary>
internal sealed record PushDevice(Guid Id, string Endpoint, string P256dh, string Auth);

internal enum PushDeviceOutcome
{
    /// <summary>The push service accepted the message (2xx).</summary>
    Sent,

    /// <summary>The subscription has expired or was never valid here (404, 410, or a target the guard refuses): it is removed.</summary>
    Gone,

    /// <summary>The push service refused the instance's key (401, 403): the device subscribed with another key; removed after three in a row.</summary>
    Rejected,

    /// <summary>Another answer that another attempt will not change (other 4xx).</summary>
    Refused,

    /// <summary>Worth another attempt: unreachable, timed out, 408, 429 or 5xx.</summary>
    Unavailable,
}

/// <param name="Id">The subscription.</param>
/// <param name="Outcome">What its push service answered.</param>
/// <param name="Status">The HTTP status, or <c>blocked</c>, <c>unreachable</c>, <c>timeout</c>, <c>error</c>.</param>
internal sealed record PushDeviceResult(Guid Id, PushDeviceOutcome Outcome, string Status);

/// <summary>How long all devices of one push message may take together; shorter in tests.</summary>
internal sealed class PushOptions
{
    public TimeSpan Budget { get; init; } = WebPushChannel.Budget;
}

/// <summary>
/// Web Push (master plan 8.5, ADR 041): one encrypted message per subscribed device of the member, signed with
/// the instance's VAPID key. The content is end-to-end encrypted (RFC 8291); <c>Urgency: high</c> only for level 5;
/// expired subscriptions are removed. Push endpoints are third-party addresses: the connection goes through the
/// outbound guard, never to an internal address, and requests are not logged because an endpoint is a capability.
/// </summary>
internal sealed partial class WebPushChannel(IHttpClientFactory http, VapidKeyStore vapid, PushOptions options, TimeProvider time, ILogger<WebPushChannel> logger) : INotificationChannel
{
    public const string ClientName = "notify.push";

    /// <summary>How long a push service keeps a message for a device that is offline; the inbox in the app keeps it longer.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromDays(1);

    /// <summary>A subscription the push service refused our key for this many times in a row is removed.</summary>
    public const int RejectionsBeforeRemoval = 3;

    /// <summary>
    /// All devices of a message together get this long, well inside the delivery job's minute (ADR 041); a device
    /// that has not answered by then counts as unavailable, and the others' answers are kept.
    /// </summary>
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
        // In parallel, within one budget: twenty devices must not add up to more than the job may take.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(options.Budget);
        var devices = await Task.WhenAll(message.PushDevices.Select(device => SendAsync(device, payload, urgency, topic, budget.Token)));
        return Summarize(devices);
    }

    /// <summary>Sends one encrypted message to one device; every failure is an outcome, never an exception.</summary>
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
            // Never into the private network, whatever the endpoint's host resolves to (master plan 9.5).
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
            // The client's timeout, the budget or the job's end: an outcome like any other, so that the answers of the
            // other devices are still written.
            return new PushDeviceResult(device.Id, PushDeviceOutcome.Unavailable, "timeout");
        }
        catch (Exception ex)
        {
            LogFailed(device.Id, ex.GetType().Name);
            return new PushDeviceResult(device.Id, PushDeviceOutcome.Unavailable, "error");
        }
    }

    /// <summary>
    /// Sent when any device accepted; otherwise worth another attempt when any failure was passing, and final
    /// when every device answered for good (master plan 8.6).
    /// </summary>
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

    /// <summary>
    /// Writes what the devices answered into their subscriptions, in the caller's unit of work: success resets the
    /// count, an expired subscription is removed, a refused key counts towards removal, any other failure counts.
    /// </summary>
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
                    // Any other failure breaks a run of refusals: only refusals in a row remove a device.
                    await row.ExecuteUpdateAsync(
                        s => s.SetProperty(x => x.FailedCount, x => x.FailedCount + 1).SetProperty(x => x.RejectedCount, 0), cancellationToken);
                    break;
            }
        }
    }

    /// <summary>
    /// A push service replaces a message it still holds with a newer one of the same topic (RFC 8030 5.4): a retry of
    /// the same delivery does not pile up on an offline device. At most 32 base64url characters.
    /// </summary>
    public static string Topic(Guid deliveryId) => WebEncoders.Base64UrlEncode(deliveryId.ToByteArray());

    /// <summary>
    /// The push services of the browser makers (master plan 9.5): Google (Chrome, Edge on Android, Opera, Samsung),
    /// Mozilla, Apple and Microsoft (Edge), plus hosts the operator named for a self-hosted push service.
    /// </summary>
    public static bool IsPushService(string host, IReadOnlyCollection<string> configured)
    {
        host = host.TrimEnd('.').ToLowerInvariant();
        return host is "fcm.googleapis.com" or "updates.push.services.mozilla.com" or "web.push.apple.com"
            || host.EndsWith(".push.apple.com", StringComparison.Ordinal)
            || host.EndsWith(".notify.windows.com", StringComparison.Ordinal)
            || configured.Contains(host, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Checks a subscription's endpoint when it is saved: https, no credentials or fragment, a known push service. No DNS here; the guard checks when sending.</summary>
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

    /// <summary>The SHA-256 of an endpoint: the unique index holds it instead of the long address.</summary>
    public static string Hash(string endpoint) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));

    [LoggerMessage(EventId = 6110, Level = LogLevel.Information, Message = "Push subscription {SubscriptionId} was removed: its push service answered {Status}")]
    private partial void LogRemoved(Guid subscriptionId, string status);

    [LoggerMessage(EventId = 6111, Level = LogLevel.Warning, Message = "Sending to push subscription {SubscriptionId} failed with {Error}")]
    private partial void LogFailed(Guid subscriptionId, string error);
}
