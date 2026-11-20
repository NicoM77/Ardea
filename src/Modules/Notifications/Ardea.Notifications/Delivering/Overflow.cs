using Ardea.Notifications.Evaluation;
using Ardea.Notifications.Persistence;
using Ardea.Platform.Jobs;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Notifications.Delivering;

internal sealed record SendOverflow(string RecipientKey, string Channel) : IJob
{
    public static string Kind => "notify.send-overflow";

    public static EnqueueOptions Options(Guid organizationId, string recipientKey, string channel, DateTimeOffset runAt) =>
        new() { CoalesceKey = $"notify.send-overflow:{organizationId}:{recipientKey}:{channel}", OrganizationId = organizationId, RunAt = runAt };
}

internal sealed class SendOverflowHandler(NotifyDbContext db, IJobQueue jobs, ITenantContext tenant, TimeProvider time) : IJobHandler<SendOverflow>
{
    public async Task HandleAsync(SendOverflow job, JobContext context, CancellationToken cancellationToken)
    {
        var organization = tenant.RequiredOrganizationId;
        await EvaluateEventsHandler.LockOrganizationAsync(db, organization, cancellationToken);
        var capped = await db.DeliveryEvents
            .Where(e => e.RecipientKey == job.RecipientKey && e.Channel == job.Channel)
            .Join(
                db.Deliveries.Where(d => d.Kind == "immediate" && d.State == "suppressed" && d.LastError == Evaluator.CappedReason),
                e => e.DeliveryId,
                d => d.Id,
                (e, d) => e)
            .ToListAsync(cancellationToken);
        var eventIds = capped.Select(e => e.EventId).ToArray();
        var inDigest = (await db.DigestItems.AsNoTracking()
                .Where(i => eventIds.Contains(i.EventId))
                .Join(
                    db.Deliveries.AsNoTracking().Where(d => d.Kind == "digest" && d.RecipientKey == job.RecipientKey && d.Channel == job.Channel
                        && d.State != "dead" && d.State != "suppressed"),
                    i => new { Target = (Guid?)i.RuleTargetId, End = i.PeriodEnd },
                    d => new { Target = d.RuleTargetId, End = d.PeriodEnd },
                    (i, d) => i.EventId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var overflow = capped.Where(e => !inDigest.Contains(e.EventId)).ToList();
        if (overflow.Count == 0)
        {
            return;
        }

        var delivery = await Evaluator.WaitingAsync(db, organization, job.RecipientKey, job.Channel, cancellationToken);
        if (delivery is null)
        {
            var now = time.GetUtcNow();
            var message = new Delivery
            {
                Id = Guid.CreateVersion7(),
                Kind = "immediate",
                Channel = job.Channel,
                RecipientKey = job.RecipientKey,
                SendAfter = now,
                State = "pending",
                CreatedAt = now,
            };
            db.Deliveries.Add(message);
            await db.SaveChangesAsync(cancellationToken);
            await jobs.EnqueueAsync(new Deliver(message.Id), Deliver.Options(message.Id, now), cancellationToken);
            delivery = message.Id;
        }

        foreach (var item in overflow)
        {
            item.DeliveryId = delivery.Value;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
