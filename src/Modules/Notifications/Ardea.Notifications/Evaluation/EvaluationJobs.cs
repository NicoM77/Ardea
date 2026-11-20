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

internal sealed record EvaluateEvents : IJob
{
    public static string Kind => "notify.evaluate";

    public const int Batch = 200;

    public const int MaxEventAttempts = 5;

    public static readonly TimeSpan Stale = TimeSpan.FromMinutes(2);

    public static EnqueueOptions Options(Guid organizationId) =>
        new() { CoalesceKey = $"notify.evaluate:{organizationId}", OrganizationId = organizationId, Priority = JobPriority.Urgent };
}

internal sealed partial class EvaluateEventsHandler(
    NotifyDbContext db, Evaluator evaluator, ITenantContext tenant, IJobQueue jobs, TimeProvider time, ILogger<EvaluateEventsHandler> logger)
    : IJobHandler<EvaluateEvents>
{
    public async Task HandleAsync(EvaluateEvents job, JobContext context, CancellationToken cancellationToken)
    {
        var organization = tenant.RequiredOrganizationId;
        await LockOrganizationAsync(db, organization, cancellationToken);
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

    internal static async Task LockOrganizationAsync(NotifyDbContext db, Guid organization, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"notify.evaluate:" + organization}, 0))", cancellationToken);

    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning, Message = "Evaluating event {EventId} ({Kind}) failed, attempt {Attempt}; the organisation's other events went on")]
    private partial void LogEventFailed(Exception exception, Guid eventId, string kind, int attempt);
}

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

internal sealed record DistributeWarning(Guid EntryId) : IJob
{
    public static string Kind => "notify.distribute-warning";

    public static readonly TimeSpan Freshness = TimeSpan.FromDays(14);

    public static EnqueueOptions Options(Guid entryId) => new() { CoalesceKey = $"notify.distribute-warning:{entryId}" };
}

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
