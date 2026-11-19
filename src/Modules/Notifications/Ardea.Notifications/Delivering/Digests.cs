using Ardea.Assessment.Contracts;
using Ardea.Iam.Contracts;
using Ardea.Notifications.Evaluation;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Email;
using Ardea.Platform.Jobs;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Notifications.Delivering;

/// <summary>
/// The instance's recurring job (master plan 8.4, 9.2): asks which digest targets are due, through the narrow
/// function <c>notify.due_digest_targets</c>, and queues one job per target in its organisation. As a safety net it
/// also queues the evaluation of organisations whose events have waited too long
/// (<c>notify.organizations_with_waiting_events</c>): a lost wake-up, or an event that failed and is tried again.
/// </summary>
internal sealed record FindDueDigests : IJob
{
    public static string Kind => "notify.find-due-digests";
}

internal sealed class FindDueDigestsHandler(NotifyDbContext db, IJobQueue jobs, TimeProvider time) : IJobHandler<FindDueDigests>
{
    public async Task HandleAsync(FindDueDigests job, JobContext context, CancellationToken cancellationToken)
    {
        var due = await db.Database.SqlQuery<DueTarget>($"""
            SELECT organization_id, target_id FROM notify.due_digest_targets({time.GetUtcNow()})
            """).ToListAsync(cancellationToken);
        foreach (var target in due)
        {
            await jobs.EnqueueAsync(new SendDigest(target.TargetId), SendDigest.Options(target.OrganizationId, target.TargetId), cancellationToken);
        }

        var waiting = await db.Database.SqlQuery<Guid>($"""
            SELECT organization_id AS "Value" FROM notify.organizations_with_waiting_events({time.GetUtcNow() - EvaluateEvents.Stale})
            """).ToListAsync(cancellationToken);
        foreach (var organization in waiting)
        {
            await jobs.EnqueueAsync(new EvaluateEvents(), EvaluateEvents.Options(organization), cancellationToken);
        }
    }

    internal sealed record DueTarget(Guid OrganizationId, Guid TargetId);
}

/// <summary><c>SendDigest(targetId)</c> of master plan 3.3: one digest per recipient of the target for the period that just ended.</summary>
internal sealed record SendDigest(Guid TargetId) : IJob
{
    public static string Kind => "notify.send-digest";

    public static EnqueueOptions Options(Guid organizationId, Guid targetId) =>
        new() { CoalesceKey = $"notify.send-digest:{targetId}", OrganizationId = organizationId };
}

/// <summary>
/// Closes the target's period: everything it noted since the last digest belongs to this one, the recipients
/// are resolved now, and each gets one message, unique for target, recipient and period end (master plan 8.4).
/// A target without news sends only when it is set to send empty digests; the empty digest is then a message
/// like any other and stands in the log.
/// </summary>
internal sealed class SendDigestHandler(
    NotifyDbContext db,
    IActionFacts facts,
    IOrganizationProfiles organizations,
    IOrganizationMembers members,
    IEmailSender email,
    IJobQueue jobs,
    ITenantContext tenant,
    TimeProvider time) : IJobHandler<SendDigest>
{
    public async Task HandleAsync(SendDigest job, JobContext context, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var target = await db.Targets.SingleOrDefaultAsync(t => t.Id == job.TargetId && t.Mode == "digest", cancellationToken);
        var profile = await organizations.FindAsync(tenant.RequiredOrganizationId, cancellationToken);
        if (target is null || profile is null || target.DigestCron is null)
        {
            return;
        }

        if (target.NextDigestAt is not { } periodEnd)
        {
            // Written without a schedule (the migration for existing organisations): the first period starts now.
            target.NextDigestAt = DigestSchedule.Next(target.DigestCron, profile.TimeZone, now);
            target.LastDigestAt ??= now;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (periodEnd > now)
        {
            return;
        }

        var periodStart = target.LastDigestAt ?? target.CreatedAt;
        var rule = await db.Rules.AsNoTracking().SingleAsync(r => r.Id == target.RuleId, cancellationToken);
        await db.DigestItems.Where(i => i.RuleTargetId == target.Id && i.PeriodEnd == null && i.OccurredAt <= periodEnd)
            .ExecuteUpdateAsync(i => i.SetProperty(x => x.PeriodEnd, periodEnd), cancellationToken);
        var hasItems = await db.DigestItems.AnyAsync(i => i.RuleTargetId == target.Id && i.PeriodEnd == periodEnd, cancellationToken);
        var hasGrowth = !hasItems && await HasGrowthAsync(rule, periodStart, periodEnd, cancellationToken);

        if (rule.Enabled && (hasItems || hasGrowth || target.SendEmpty) && (target.Channel != Channels.Email || await email.IsConfiguredAsync(cancellationToken)))
        {
            var directory = await RecipientDirectory.LoadAsync(db, members, cancellationToken);
            foreach (var recipient in directory.Resolve(target))
            {
                if (directory.IsMuted(recipient, target.Channel, null, profile.AllowMuteCritical)
                    || await db.Deliveries.AnyAsync(d => d.Kind == "digest" && d.RuleTargetId == target.Id && d.RecipientKey == recipient.Key && d.PeriodEnd == periodEnd, cancellationToken))
                {
                    continue;
                }

                var delivery = new Delivery
                {
                    Id = Guid.CreateVersion7(),
                    Kind = "digest",
                    Channel = target.Channel,
                    RecipientKey = recipient.Key,
                    SendAfter = now,
                    State = "pending",
                    CreatedAt = now,
                    RuleTargetId = target.Id,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                };
                db.Deliveries.Add(delivery);
                await jobs.EnqueueAsync(new Deliver(delivery.Id), Deliver.Options(delivery.Id, now), cancellationToken);
            }
        }

        target.LastDigestAt = periodEnd;
        // A worker that was down for a while sends one digest for the whole gap, not one per missed occurrence.
        target.NextDigestAt = DigestSchedule.Next(target.DigestCron, profile.TimeZone, periodEnd > now ? periodEnd : now);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> HasGrowthAsync(NotificationRule rule, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var growth = await facts.FindingsJoinedAsync(start, end, cancellationToken);
        if (growth.Count == 0)
        {
            return false;
        }

        return (await facts.ActionsAsync([.. growth.Select(g => g.RemediationId)], cancellationToken))
            .Any(a => a.Status != "closed" && a.Level >= rule.MinLevel && (rule.ProjectIds.Length == 0 || rule.ProjectIds.Contains(a.ProjectId)));
    }
}
