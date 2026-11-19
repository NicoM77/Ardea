using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Ardea.Notifications.Evaluation;
using Ardea.Notifications.Messages;
using Ardea.Notifications.Rules;
using Ardea.Platform.Email;
using Ardea.Platform.Http;
using MailKit.Net.Smtp;

namespace Ardea.Notifications.Delivering;

internal enum SendOutcome
{
    Sent,

    /// <summary>Worth another attempt: the server was not reachable, timed out or asked to slow down.</summary>
    Transient,

    /// <summary>Another attempt gives the same answer (master plan 8.6): unknown address, 4xx except 429.</summary>
    Permanent,

    /// <summary>The channel is off: nothing was tried.</summary>
    Suppressed,
}

/// <param name="Outcome">What happened.</param>
/// <param name="Error">Why it did not work, without secrets.</param>
/// <param name="Status">For webhooks: the HTTP status or the kind of failure, shown at the endpoint.</param>
/// <param name="Devices">For push: what each device's push service answered, written to its subscription.</param>
internal sealed record SendResult(SendOutcome Outcome, string? Error = null, string? Status = null, IReadOnlyList<PushDeviceResult>? Devices = null);

/// <summary>A message ready to go out: content and where it goes; read before the call, sent without a transaction.</summary>
internal sealed record Outgoing
{
    public required MessageContent Content { get; init; }

    public required Recipient Recipient { get; init; }

    public required string Channel { get; init; }

    /// <summary>For e-mail: the member's settings, or the signed unsubscribe page for an external address.</summary>
    public Uri? Unsubscribe { get; init; }

    /// <summary>For external addresses: the RFC 8058 one-click address.</summary>
    public Uri? OneClick { get; init; }

    public Uri? WebhookUrl { get; init; }

    public string? WebhookSecret { get; init; }

    /// <summary>The instance administrator released the webhook's host for private addresses (master plan 9.5).</summary>
    public bool AllowInternal { get; init; }

    /// <summary>For push: the member's subscribed devices, read in the first step.</summary>
    public IReadOnlyList<PushDevice> PushDevices { get; init; } = [];
}

/// <summary>
/// One way to reach a recipient (master plan 3.6, 8.5): one class per channel. In-app notifications are
/// written in the evaluation's own transaction and need no class.
/// </summary>
internal interface INotificationChannel
{
    string Name { get; }

    Task<SendResult> SendAsync(Outgoing message, CancellationToken cancellationToken);
}

/// <summary>E-mail through the instance's SMTP server (master plan 8.5); without SMTP the channel is off.</summary>
internal sealed class EmailChannel(IEmailSender email) : INotificationChannel
{
    public string Name => Channels.Email;

    public async Task<SendResult> SendAsync(Outgoing message, CancellationToken cancellationToken)
    {
        if (message.Recipient.Email is not { Length: > 0 } to)
        {
            return new SendResult(SendOutcome.Permanent, "The recipient has no e-mail address.");
        }

        if (!await email.IsConfiguredAsync(cancellationToken))
        {
            return new SendResult(SendOutcome.Suppressed, "SMTP is not configured.");
        }

        try
        {
            await email.SendAsync(MessageRenderer.Email(message.Content, to, message.Unsubscribe!, message.OneClick), cancellationToken);
            return new SendResult(SendOutcome.Sent);
        }
        catch (SmtpCommandException ex) when (ex.ErrorCode == SmtpErrorCode.RecipientNotAccepted && (int)ex.StatusCode >= 500)
        {
            return new SendResult(SendOutcome.Permanent, $"The mail server refused the recipient: {(int)ex.StatusCode} {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Every other failure (connection, TLS handshake, authentication, a reply the client cannot parse) may
            // pass: try again. The job system's back-off and last attempt decide when to give up.
            return new SendResult(SendOutcome.Transient, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}

/// <summary>
/// <c>POST</c> with JSON, schema <c>ardea.notification.v1</c> (master plan 8.5). The header
/// <c>Ardea-Signature: t=&lt;unix seconds&gt;,v1=&lt;hex&gt;</c> carries HMAC-SHA256 over <c>"{t}.{body}"</c> with the
/// endpoint's secret. The connection goes only to checked addresses, private ones only for hosts the
/// instance administrator released, and follows no redirect (master plan 9.5).
/// </summary>
internal sealed class WebhookChannel(IHttpClientFactory http, TimeProvider time) : INotificationChannel
{
    public const string ClientName = "notify.webhook";

    public const string SignatureHeader = "Ardea-Signature";

    private const string BlockedTarget = "resolves to no address Ardea may connect to";

    public string Name => Channels.Webhook;

    public async Task<SendResult> SendAsync(Outgoing message, CancellationToken cancellationToken)
    {
        if (message.WebhookUrl is not { } url || message.WebhookSecret is not { } secret)
        {
            return new SendResult(SendOutcome.Permanent, "The endpoint's address or secret cannot be read.");
        }

        var body = MessageRenderer.Webhook(message.Content);
        return await PostAsync(url, secret, body, message.Content.DeliveryId, message.AllowInternal, cancellationToken);
    }

    public async Task<SendResult> PostAsync(Uri url, string secret, string body, Guid deliveryId, bool allowInternal, CancellationToken cancellationToken)
    {
        var timestamp = time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Options.Set(OutboundAddresses.AllowInternal, allowInternal);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Ardea", null));
        request.Headers.Add("Ardea-Delivery", deliveryId.ToString());
        request.Headers.Add(SignatureHeader, $"t={timestamp},v1={Sign(secret, timestamp, body)}");

        var watch = Stopwatch.StartNew();
        try
        {
            using var client = http.CreateClient(ClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            return (int)response.StatusCode switch
            {
                >= 200 and < 300 => new SendResult(SendOutcome.Sent, Status: status),
                408 or 429 or >= 500 => new SendResult(SendOutcome.Transient, $"The endpoint answered {status}.", status),
                >= 300 and < 400 => new SendResult(SendOutcome.Permanent, $"The endpoint answered {status}; redirects are not followed.", status),
                _ => new SendResult(SendOutcome.Permanent, $"The endpoint answered {status}.", status),
            };
        }
        catch (HttpRequestException ex) when (ex.Message.Contains(BlockedTarget, StringComparison.Ordinal) || ex.InnerException?.Message.Contains(BlockedTarget, StringComparison.Ordinal) == true)
        {
            return new SendResult(SendOutcome.Permanent, "The endpoint's host resolves to an internal address that is not released for webhooks.", "blocked");
        }
        catch (HttpRequestException ex)
        {
            return new SendResult(SendOutcome.Transient, $"The endpoint could not be reached: {ex.HttpRequestError}.", "unreachable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SendResult(SendOutcome.Transient, $"The endpoint did not answer within {watch.Elapsed.TotalSeconds:0} seconds.", "timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SendResult(SendOutcome.Transient, $"{ex.GetType().Name}: {ex.Message}", "error");
        }
    }

    /// <summary>Lower-case hex of HMAC-SHA256 over <c>"{timestamp}.{body}"</c>; what a receiver computes to check a message.</summary>
    public static string Sign(string secret, string timestamp, string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));

    /// <summary>A new shared secret: 32 random bytes, base64url, prefixed so that people recognise it.</summary>
    public static string NewSecret() => "whsec_" + Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Checks a webhook address when it is saved (master plan 9.5): http or https, no credentials or fragment,
    /// and a host that resolves to no forbidden address, and to a private one only when the administrator released it.
    /// </summary>
    public static async Task<string?> CheckAddressAsync(Uri url, IReadOnlyCollection<string> releasedHosts, CancellationToken cancellationToken)
    {
        if (url.Scheme is not ("https" or "http") || url.UserInfo.Length > 0 || url.Fragment.Length > 0)
        {
            return "Enter an http or https address without credentials or fragment.";
        }

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(url.Host.Trim('[', ']'), out var literal) ? [literal] : await Dns.GetHostAddressesAsync(url.IdnHost, cancellationToken);
        }
        catch (System.Net.Sockets.SocketException)
        {
            return "The host name cannot be resolved.";
        }

        if (addresses.Length == 0 || addresses.Any(OutboundAddresses.IsForbidden))
        {
            return "The host resolves to an address Ardea must never call.";
        }

        if (addresses.Any(OutboundAddresses.IsInternal) && !IsReleased(url, releasedHosts))
        {
            return "The host resolves to an internal address. The instance administrator must release it for webhooks first.";
        }

        return null;
    }

    public static bool IsReleased(Uri url, IReadOnlyCollection<string> releasedHosts) =>
        releasedHosts.Contains(url.IdnHost.Trim('[', ']'), StringComparer.OrdinalIgnoreCase);
}
