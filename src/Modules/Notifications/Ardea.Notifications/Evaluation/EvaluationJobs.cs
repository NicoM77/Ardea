using Ardea.Assessment.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Jobs;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
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

    public static EnqueueOptions Options(Guid organizationId) =>
        new() { CoalesceKey = $"notify.evaluate:{organizationId}", OrganizationId = organizationId, Priority = JobPriority.Urgent };
}

internal sealed class EvaluateEventsHandler(NotifyDbContext db, Evaluator evaluator, ITenantContext tenant, IJobQueue jobs) : IJobHandler<EvaluateEvents>
{
    public async Task HandleAsync(EvaluateEvents job, JobContext context, CancellationToken cancellationToken)
    {
        var organization = tenant.RequiredOrganizationId;
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"notify.evaluate:" + organization}, 0))", cancellationToken);
        var pending = await db.PendingEvents.OrderBy(p => p.At).ThenBy(p => p.EventId).Take(EvaluateEvents.Batch).ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        await evaluator.EvaluateAsync(pending, cancellationToken);
        db.PendingEvents.RemoveRange(pending);
        await db.SaveChangesAsync(cancellationToken);
        if (pending.Count == EvaluateEvents.Batch)
        {
            await jobs.EnqueueAsync(new EvaluateEvents(), EvaluateEvents.Options(organization), cancellationToken);
        }
    }
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
