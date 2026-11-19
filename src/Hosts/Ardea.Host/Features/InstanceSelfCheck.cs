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
/// could not be sent gives its claim back in a new short unit of work, so the next round tries again. It runs in
/// the <c>api</c> and the <c>worker</c> role and needs no job: a stalled worker cannot report itself. One process
/// at a time does a round (session advisory lock on a connection of its own, try-lock, skip if taken).
/// </summary>
internal sealed partial class InstanceSelfCheck(
    NpgsqlDataSource dataSource, IServiceScopeFactory scopes, TimeProvider time, IConfiguration configuration, ILogger<InstanceSelfCheck> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private const long LockKey = 0x41524445_53454C46; // "ARDESELF"

    private bool _toldNoSmtp;
    private bool _toldNoRecipients;

    /// <summary>One round; false when another process is doing one right now.</summary>
    public async Task<bool> RunRoundAsync(CancellationToken cancellationToken)
    {
        await using var session = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand($"SELECT pg_try_advisory_lock({LockKey})", session))
        {
            if (!(bool)(await command.ExecuteScalarAsync(cancellationToken))!)
            {
                LogSkipped();
                return false;
            }
        }

        try
        {
            await RoundAsync(cancellationToken);
        }
        finally
        {
            try
            {
                await using var unlock = new NpgsqlCommand($"SELECT pg_advisory_unlock({LockKey})", session);
                await unlock.ExecuteScalarAsync(CancellationToken.None);
            }
            catch (NpgsqlException ex)
            {
                // A broken session has ended, and with it the lock.
                LogUnlockFailed(ex);
            }
        }

        return true;
    }

    private async Task RoundAsync(CancellationToken cancellationToken)
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
            var status = new InstanceStatusBuilder();
            foreach (var contributor in scope.ServiceProvider.GetServices<IInstanceStatusContributor>())
            {
                await contributor.ContributeAsync(status, cancellationToken);
            }

            active = status.Warnings.Count;
            mail = await scope.ServiceProvider.GetRequiredService<InstanceWarningLedger>()
                .ReconcileAsync(status.Warnings, now, claim: recipients.Count > 0, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        }

        TellWhyNobodyHears(active, configured, recipients.Count);
        if (mail.IsEmpty)
        {
            return;
        }

        // No transaction is open from here on.
        var failed = 0;
        try
        {
            foreach (var recipient in recipients)
            {
                try
                {
                    await email.SendAsync(Render(recipient, mail), cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    LogSendFailed(recipient.UserId, ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            await ReleaseAsync(mail, CancellationToken.None);
            throw;
        }

        if (failed > 0)
        {
            // Everybody gets it again with the next round: a duplicate rather than a lost warning.
            await ReleaseAsync(mail, cancellationToken);
            return;
        }

        LogMailed(recipients.Count, mail.Raised.Count, mail.Reminded.Count, mail.Resolved.Count);
    }

    private async Task ReleaseAsync(WarningMail mail, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InstanceWarningLedger>().ReleaseAsync(mail, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
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

    [LoggerMessage(EventId = 1504, Level = LogLevel.Information, Message = "Mailed {Recipients} instance administrator(s) about {Raised} new, {Reminded} lasting and {Resolved} resolved warning(s)")]
    private partial void LogMailed(int recipients, int raised, int reminded, int resolved);

    [LoggerMessage(EventId = 1505, Level = LogLevel.Warning, Message = "The mail about instance warnings to administrator {UserId} failed with {Error}; the next round tries again")]
    private partial void LogSendFailed(Guid userId, string error);

    [LoggerMessage(EventId = 1506, Level = LogLevel.Warning, Message = "The self-check's lock could not be released")]
    private partial void LogUnlockFailed(Exception exception);
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
