using Ardea.Assessment.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Jobs;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ardea.Notifications.Evaluation;

/// <summary>
/// <c>EvaluateRules</c> of master plan 3.3 for one organisation: takes its pending events in the order they
/// happened. One waiting job per organisation; a lock keeps a running and a following one apart.
/// </summary>
internal sealed record EvaluateEvents : IJob
{
    public static string Kind => "notify.evaluate";

    public const int Batch = 200;

    /// <summary>An event whose evaluation failed this often is set aside; the organisation's other events go on.</summary>
    public const int MaxEventAttempts = 5;

    /// <summary>Events waiting longer than this are queued again by the instance's sweep (<c>notify.find-due-digests</c>).</summary>
    public static readonly TimeSpan Stale = TimeSpan.FromMinutes(2);

    public static EnqueueOptions Options(Guid organizationId) =>
        new() { CoalesceKey = $"notify.evaluate:{organizationId}", OrganizationId = organizationId, Priority = JobPriority.Urgent };
}

/// <summary>
/// Each event is evaluated under a savepoint of its own: an event that fails is rolled back alone, counted, and tried
/// again when the sweep finds it waiting, so one broken event does not hold back its organisation's notifications. After
/// <see cref="EvaluateEvents.MaxEventAttempts"/> it is set aside with its error (instance status, ADR 038).
/// </summary>
internal sealed partial class EvaluateEventsHandler(
    NotifyDbContext db, Evaluator evaluator, ITenantContext tenant, IJobQueue jobs, TimeProvider time, ILogger<EvaluateEventsHandler> logger)
    : IJobHandler<EvaluateEvents>
{
    public async Task HandleAsync(EvaluateEvents job, JobContext context, CancellationToken cancellationToken)
    {
        var organization = tenant.RequiredOrganizationId;
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"notify.evaluate:" + organization}, 0))", cancellationToken);
        var pending = await db.PendingEvents.AsNoTracking().Where(p => p.FailedAt == null)
            .OrderBy(p => p.At).ThenBy(p => p.EventId).Take(EvaluateEvents.Batch).ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        var batch = await evaluator.LoadAsync(pending, cancellationToken);
        foreach (var next in pending)
        {
            await db.Database.ExecuteSqlRawAsync("SAVEPOINT notify_event", cancellationToken);
            try
            {
                if (batch is not null)
                {
                    await evaluator.EvaluateAsync(batch, next, cancellationToken);
                }

                await db.PendingEvents.Where(p => p.EventId == next.EventId).ExecuteDeleteAsync(cancellationToken);
                await db.Database.ExecuteSqlRawAsync("RELEASE SAVEPOINT notify_event", cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                await db.Database.ExecuteSqlRawAsync("ROLLBACK TO SAVEPOINT notify_event", cancellationToken);
                db.ChangeTracker.Clear();
                var attempts = next.Attempts + 1;
                var error = $"{ex.GetType().Name}: {ex.Message}";
                error = error.Length > 2000 ? error[..2000] : error;
                var failedAt = attempts >= EvaluateEvents.MaxEventAttempts ? time.GetUtcNow() : (DateTimeOffset?)null;
                await db.PendingEvents.Where(p => p.EventId == next.EventId).ExecuteUpdateAsync(
                    p => p.SetProperty(x => x.Attempts, attempts).SetProperty(x => x.LastError, error).SetProperty(x => x.FailedAt, failedAt),
                    cancellationToken);
                LogEventFailed(ex, next.EventId, next.Kind, attempts);
            }
        }

        if (pending.Count == EvaluateEvents.Batch)
        {
            await jobs.EnqueueAsync(new EvaluateEvents(), EvaluateEvents.Options(organization), cancellationToken);
        }
    }

    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning, Message = "Evaluating event {EventId} ({Kind}) failed, attempt {Attempt}; the organisation's other events went on")]
    private partial void LogEventFailed(Exception exception, Guid eventId, string kind, int attempt);
}

/// <summary>
/// Hears about the events Assessment wrote, inside its transaction: they wait in <c>pending_event</c> and the
/// organisation's evaluation job is queued, so neither exists without the other (master plan 3.3, 3.4).
/// </summary>
internal sealed class ActionEventInbox(NotifyDbContext db, IJobQueue jobs, ITenantContext tenant) : IActionEventSubscriber
{
    public async Task OnActionEventsAsync(IReadOnlyList<NotifiableEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        var organization = tenant.RequiredOrganizationId;
        foreach (var e in events)
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO notify.pending_event (event_id, organization_id, kind, at) VALUES ({e.Id}, {organization}, {e.Kind}, {e.At})
                ON CONFLICT (organization_id, event_id) DO NOTHING
                """, cancellationToken);
        }

        await jobs.EnqueueAsync(new EvaluateEvents(), EvaluateEvents.Options(organization), cancellationToken);
    }
}

/// <summary>A new warning without identifier is distributed once for the instance (master plan 8.2, 9.2).</summary>
internal sealed record DistributeWarning(Guid EntryId) : IJob
{
    public static string Kind => "notify.distribute-warning";

    /// <summary>Older warnings arrive with the first load of a source; they are history, not news.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromDays(14);

    public static EnqueueOptions Options(Guid entryId) => new() { CoalesceKey = $"notify.distribute-warning:{entryId}" };
}

/// <summary>Hears about new entries from Intel and starts the distribution of new warnings.</summary>
internal sealed class WarningSubscriber(IJobQueue jobs) : IEntryChangeSubscriber
{
    public async Task OnEntryChangedAsync(EntryChanged change, CancellationToken cancellationToken)
    {
        if (change.ChangeKinds.Contains("created"))
        {
            await jobs.EnqueueAsync(new DistributeWarning(change.EntryId), DistributeWarning.Options(change.EntryId), cancellationToken);
        }
    }
}

/// <summary>
/// A system job (master plan 9.2): asks which organisations have a rule for warnings, through the narrow
/// function <c>notify.organizations_with_warning_rules</c>, and queues one job per organisation.
/// </summary>
internal sealed class DistributeWarningHandler(IEntryQueries entries, NotifyDbContext db, IJobQueue jobs, TimeProvider time) : IJobHandler<DistributeWarning>
{
    public async Task HandleAsync(DistributeWarning job, JobContext context, CancellationToken cancellationToken)
    {
        var entry = await entries.FindAsync(job.EntryId, cancellationToken);
        if (entry is not { Kind: "warning", State: "published" } || (entry.PublishedAt ?? entry.ChangedAt) < time.GetUtcNow() - DistributeWarning.Freshness)
        {
            return;
        }

        var organizations = await db.Database.SqlQuery<Guid>($"""SELECT organization_id AS "Value" FROM notify.organizations_with_warning_rules()""")
            .ToListAsync(cancellationToken);
        foreach (var organization in organizations)
        {
            await jobs.EnqueueAsync(new ReceiveWarning(job.EntryId), ReceiveWarning.Options(organization, job.EntryId), cancellationToken);
        }
    }
}

/// <summary>A warning reaches one organisation: the entry's id is the event's id, so it is evaluated there once.</summary>
internal sealed record ReceiveWarning(Guid EntryId) : IJob
{
    public static string Kind => "notify.receive-warning";

    public static EnqueueOptions Options(Guid organizationId, Guid entryId) =>
        new() { CoalesceKey = $"notify.receive-warning:{organizationId}:{entryId}", OrganizationId = organizationId };
}

internal sealed class ReceiveWarningHandler(NotifyDbContext db, IJobQueue jobs, ITenantContext tenant, TimeProvider time) : IJobHandler<ReceiveWarning>
{
    public async Task HandleAsync(ReceiveWarning job, JobContext context, CancellationToken cancellationToken)
    {
        var organization = tenant.RequiredOrganizationId;
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO notify.pending_event (event_id, organization_id, kind, at, entry_id)
            VALUES ({job.EntryId}, {organization}, {NotifyEvents.WarningPublished}, {time.GetUtcNow()}, {job.EntryId})
            ON CONFLICT (organization_id, event_id) DO NOTHING
            """, cancellationToken);
        await jobs.EnqueueAsync(new EvaluateEvents(), EvaluateEvents.Options(organization), cancellationToken);
    }
}

internal static class PostgresErrors
{
    public static bool IsUniqueViolation(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };
}
