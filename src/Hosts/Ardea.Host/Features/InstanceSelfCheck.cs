using System.Globalization;
using Ardea.Iam.Contracts;
using Ardea.Platform.Email;
using Ardea.Platform.Persistence;
using Ardea.Platform.Status;
using Npgsql;

namespace Ardea.Host.Features;

/// <summary>
/// The instance tells its administrators when it warns about itself (master plan 12.7, task 6.6, ADR 043). A round
/// computes the status through the modules' contributors, brings <c>platform.instance_warning</c> in line with it
/// and claims what to mail in one short unit of work, commits, and only then talks to the mail server; a mail that
/// could not be sent to anyone gives its claim back in a new short unit of work, so the next round tries again. It
/// runs in the <c>api</c> and the <c>worker</c> role and needs no job: a stalled worker cannot report itself. One
/// process at a time reconciles: a transaction-level advisory try-lock in the reconciling unit of work (skip the
/// round if taken), which ends with that transaction; no lock and no connection is held while the mail is sent.
/// </summary>
internal sealed partial class InstanceSelfCheck(
    IServiceScopeFactory scopes, TimeProvider time, IConfiguration configuration, ILogger<InstanceSelfCheck> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    internal const long LockKey = 0x41524445_53454C46; // "ARDESELF"

    private bool _toldNoSmtp;
    private bool _toldNoRecipients;

    /// <summary>One round; false when another process is reconciling right now.</summary>
    public async Task<bool> RunRoundAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        // Who can be told: outside any transaction (the sender and the directory read in short units of work of their own).
        await using var outside = scopes.CreateAsyncScope();
        var email = outside.ServiceProvider.GetRequiredService<IEmailSender>();
        var configured = await email.IsConfiguredAsync(cancellationToken);
        var recipients = configured
            ? await outside.ServiceProvider.GetRequiredService<IInstanceAdministrators>().ListReachableAsync(cancellationToken)
            : [];

        // Status, state and claim in one short unit of work. The result is built before the commit.
        WarningMail mail;
        int active;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            if (!await LockAsync(unitOfWork, wait: false, cancellationToken))
            {
                LogSkipped();
                return false;
            }

            var status = new InstanceStatusBuilder();
            foreach (var contributor in scope.ServiceProvider.GetServices<IInstanceStatusContributor>())
            {
                await contributor.ContributeAsync(status, cancellationToken);
            }

            active = status.Warnings.Count;
            mail = await scope.ServiceProvider.GetRequiredService<InstanceWarningLedger>()
                .ReconcileAsync(status.Warnings, now, claim: recipients.Count > 0, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        TellWhyNobodyHears(active, configured, recipients.Count);
        if (mail.IsEmpty)
        {
            return true;
        }

        // No transaction and no lock is held from here on.
        var delivered = 0;
        try
        {
            foreach (var recipient in recipients)
            {
                try
                {
                    await email.SendAsync(Render(recipient, mail), cancellationToken);
                    delivered++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogSendFailed(recipient.UserId, ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (delivered == 0)
        {
            await ReleaseAsync(mail);
            throw;
        }

        if (delivered == 0)
        {
            // Nobody got it: the next round tries again.
            await ReleaseAsync(mail);
            return true;
        }

        // Somebody got it: the claim stands, so one address the mail server refuses for good does not make the
        // others get the same mail every five minutes. The ones it failed for hear of the warning with the reminder.
        LogMailed(delivered, recipients.Count, mail.Raised.Count, mail.Reminded.Count, mail.Resolved.Count);
        return true;
    }

    /// <summary>Gives a claim back; with no token, so a stopping process still does it.</summary>
    private async Task ReleaseAsync(WarningMail mail)
    {
        await using var scope = scopes.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await LockAsync(unitOfWork, wait: true, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<InstanceWarningLedger>().ReleaseAsync(mail, CancellationToken.None);
        await unitOfWork.CommitAsync(CancellationToken.None);
    }

    /// <summary>The transaction-level lock of the self-check's state: it ends with the unit of work's transaction.</summary>
    private static async Task<bool> LockAsync(IUnitOfWork unitOfWork, bool wait, CancellationToken cancellationToken)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            wait ? $"SELECT true FROM (SELECT pg_advisory_xact_lock({LockKey})) AS l" : $"SELECT pg_try_advisory_xact_lock({LockKey})", connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Without a mail server or an administrator with a confirmed address the warnings stay on the overview; said once.</summary>
    private void TellWhyNobodyHears(int active, bool configured, int recipients)
    {
        if (!configured)
        {
            if (active > 0 && !_toldNoSmtp)
            {
                _toldNoSmtp = true;
                LogNoSmtp(active);
            }

            return;
        }

        _toldNoSmtp = false;
        if (recipients == 0)
        {
            if (active > 0 && !_toldNoRecipients)
            {
                _toldNoRecipients = true;
                LogNoRecipients(active);
            }

            return;
        }

        _toldNoRecipients = false;
    }

    /// <summary>Plain and short: what the status shows, a link to the administration, nothing else.</summary>
    internal EmailMessage Render(InstanceAdministrator recipient, WarningMail mail)
    {
        var publicUrl = PublicUrl();
        var paragraphs = new List<string>();
        if (mail.Raised.Count > 0)
        {
            paragraphs.Add($"The Ardea instance at {publicUrl.Host} warns about itself:");
            paragraphs.AddRange(mail.Raised.Select(n => n.Message));
        }

        if (mail.Reminded.Count > 0)
        {
            paragraphs.Add(mail.Raised.Count > 0 ? "Still active:" : $"The Ardea instance at {publicUrl.Host} still warns about itself:");
            paragraphs.AddRange(mail.Reminded.Select(n => $"{n.Message} Reported since {Utc(n.FirstSeenAt)}."));
        }

        if (mail.Resolved.Count > 0)
        {
            paragraphs.Add(mail.Raised.Count + mail.Reminded.Count > 0 ? "Over:" : $"The Ardea instance at {publicUrl.Host} no longer warns about this:");
            paragraphs.AddRange(mail.Resolved.Select(n => n.Message));
        }

        paragraphs.Add("The overview and the administration (card Operation) show the warnings as long as they last.");
        var subject = mail switch
        {
            { Raised.Count: > 0 } => $"Ardea: {Count(mail.Raised.Count, "new warning")} about the instance",
            { Reminded.Count: > 0 } => $"Ardea: {Count(mail.Reminded.Count, "warning")} still active",
            _ => $"Ardea: {Count(mail.Resolved.Count, "warning")} over",
        };
        return EmailTemplate.Render(
            recipient.Email,
            subject,
            $"Hello {recipient.DisplayName},",
            paragraphs,
            "Open the administration",
            new Uri(publicUrl, "admin"),
            "You receive this e-mail as an instance administrator of " + publicUrl.Host
            + ": once when a warning is new, once a day while it lasts, and once when it is over.");
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string Utc(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private Uri PublicUrl()
    {
        var url = configuration[HostConfiguration.PublicUrlKey] ?? throw new InvalidOperationException("ARDEA_PUBLIC_URL is not set.");
        return new Uri(url.EndsWith('/') ? url : url + "/");
    }

    [LoggerMessage(EventId = 1501, Level = LogLevel.Debug, Message = "Another process is running the self-check; this round is skipped")]
    private partial void LogSkipped();

    [LoggerMessage(EventId = 1502, Level = LogLevel.Information, Message = "The instance warns about itself ({Active} warning(s)), but no mail server is set up: the administrators are not mailed, the warnings stay on the overview")]
    private partial void LogNoSmtp(int active);

    [LoggerMessage(EventId = 1503, Level = LogLevel.Information, Message = "The instance warns about itself ({Active} warning(s)), but no instance administrator has a confirmed e-mail address: the warnings stay on the overview")]
    private partial void LogNoRecipients(int active);

    [LoggerMessage(EventId = 1504, Level = LogLevel.Information, Message = "Mailed {Delivered} of {Recipients} instance administrator(s) about {Raised} new, {Reminded} lasting and {Resolved} resolved warning(s)")]
    private partial void LogMailed(int delivered, int recipients, int raised, int reminded, int resolved);

    [LoggerMessage(EventId = 1505, Level = LogLevel.Warning, Message = "The mail about instance warnings to administrator {UserId} failed with {Error}; if nobody got it, the next round tries again, otherwise the reminder")]
    private partial void LogSendFailed(Guid userId, string error);
}

/// <summary>Runs a self-check round every five minutes; the first one an interval after the start, when a worker started together with it has beaten.</summary>
internal sealed partial class InstanceSelfCheckService(InstanceSelfCheck check, TimeProvider time, ILogger<InstanceSelfCheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(InstanceSelfCheck.Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await check.RunRoundAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRoundFailed(ex);
            }
        }
    }

    [LoggerMessage(EventId = 1507, Level = LogLevel.Warning, Message = "A self-check round failed; the next one runs in five minutes")]
    private partial void LogRoundFailed(Exception exception);
}

internal static class InstanceSelfCheckServices
{
    /// <summary>In both long-running roles (ADR 043).</summary>
    public static IServiceCollection AddInstanceSelfCheck(this IServiceCollection services)
    {
        services.AddSingleton<InstanceSelfCheck>();
        services.AddHostedService<InstanceSelfCheckService>();
        return services;
    }
}
