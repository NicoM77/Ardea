using Ardea.Platform.Persistence;
using Ardea.Platform.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace Ardea.Platform.Email;

/// <summary>SMTP settings of the instance (master plan 12.4); environment variables take precedence and lock the field.</summary>
public static class SmtpSettings
{
    public static readonly SettingDefinition Host = new("smtp.host", "SMTP:HOST");
    public static readonly SettingDefinition Port = new("smtp.port", "SMTP:PORT", Default: "587");
    public static readonly SettingDefinition User = new("smtp.user", "SMTP:USER");
    public static readonly SettingDefinition Password = new("smtp.password", "SMTP:PASSWORD", IsSecret: true);
    public static readonly SettingDefinition From = new("smtp.from", "SMTP:FROM");

    /// <summary><c>starttls</c> (default), <c>tls</c> (implicit, port 465) or <c>none</c> (local mail catchers only).</summary>
    public static readonly SettingDefinition Security = new("smtp.security", "SMTP:SECURITY", Default: "starttls");

    public static IReadOnlyList<SettingDefinition> All { get; } = [Host, Port, User, Password, From, Security];
}

/// <param name="To">The recipient's address.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="Text">The plain-text part.</param>
/// <param name="Html">The HTML part.</param>
/// <param name="Headers">Further headers, e.g. <c>List-Unsubscribe</c> (master plan 8.5).</param>
public sealed record EmailMessage(string To, string Subject, string Text, string Html, IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>
/// Simple e-mail for confirmation, password reset and invitations (task 0.9). Without SMTP the
/// instance stays usable: <see cref="IsConfiguredAsync"/> is false, and callers fall back as
/// master plan 12.4 describes. Sending happens only in jobs, never inside a request.
/// </summary>
public interface IEmailSender
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken);

    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Reads its settings in a short unit of work of its own and talks to the mail server only after
/// that has ended: no transaction stays open across the call (master plan 9.2).
/// </summary>
internal sealed class SmtpEmailSender(IUnitOfWorkFactory units) : IEmailSender
{
    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken)
    {
        var values = await ReadAsync(cancellationToken);
        return !string.IsNullOrWhiteSpace(values[SmtpSettings.Host.Key]) && !string.IsNullOrWhiteSpace(values[SmtpSettings.From.Key]);
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var values = await ReadAsync(cancellationToken);
        var host = values[SmtpSettings.Host.Key] ?? throw new InvalidOperationException("SMTP is not configured.");
        var port = int.Parse(values[SmtpSettings.Port.Key] ?? "587", System.Globalization.CultureInfo.InvariantCulture);
        var user = values[SmtpSettings.User.Key];
        var password = values[SmtpSettings.Password.Key];
        var from = values[SmtpSettings.From.Key] ?? throw new InvalidOperationException("SMTP has no sender address.");
        var security = values[SmtpSettings.Security.Key]?.ToLowerInvariant() switch
        {
            "tls" => SecureSocketOptions.SslOnConnect,
            "none" => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls,
        };

        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(from));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.Text, HtmlBody = message.Html }.ToMessageBody();
        foreach (var (name, value) in message.Headers ?? new Dictionary<string, string>())
        {
            mime.Headers.Add(name, value);
        }

        using var client = new SmtpClient { Timeout = 30_000 };
        await client.ConnectAsync(host, port, security, cancellationToken);
        if (!string.IsNullOrEmpty(user))
        {
            await client.AuthenticateAsync(user, password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(new SmtpSendRequest(mime), cancellationToken);
        try
        {
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The server accepted the message with its answer to DATA: it is sent. A failing goodbye must not
            // make the caller send it again.
        }
    }

    private async Task<Dictionary<string, string?>> ReadAsync(CancellationToken cancellationToken)
    {
        await using var scope = units.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IInstanceSettings>();
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var definition in SmtpSettings.All)
        {
            values[definition.Key] = (await settings.GetAsync(definition, cancellationToken)).Value;
        }

        return values;
    }
}
