using Ardea.Notifications.Persistence;
using Ardea.Platform.Status;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Notifications.Evaluation;

internal sealed class NotifyStatusContributor(NotifyDbContext db, TimeProvider time) : IInstanceStatusContributor
{
    public async Task ContributeAsync(InstanceStatusBuilder status, CancellationToken cancellationToken)
    {
        var failed = (await db.Database.SqlQuery<long>($"""SELECT notify.failed_event_count() AS "Value" """).ToListAsync(cancellationToken))[0];
        status.Metrics.Add(new StatusMetric("notify", "events_failed", failed));
        if (failed > 0)
        {
            status.Warnings.Add(new StatusWarning("notify_events_failed", "warning",
                $"{failed} notification event(s) could not be evaluated and were set aside; see notify.pending_event."));
        }

        var since = time.GetUtcNow().AddDays(-1);
        var deliveries = await db.Database
            .SqlQuery<DeliveryCount>($"SELECT state AS \"State\", deliveries AS \"Deliveries\" FROM notify.delivery_counts_since({since})")
            .ToListAsync(cancellationToken);
        foreach (var count in deliveries.OrderBy(c => c.State, StringComparer.Ordinal))
        {
            status.Metrics.Add(new StatusMetric("deliveries", count.State, count.Deliveries));
        }

        if (deliveries.FirstOrDefault(c => c.State == "dead") is { Deliveries: > 0 } dead)
        {
            status.Warnings.Add(new StatusWarning("deliveries_failing", "warning",
                $"{dead.Deliveries} message(s) of the last 24 hours could not be delivered after every attempt; see the delivery log of the organisations concerned."));
        }
    }

    private sealed record DeliveryCount(string State, long Deliveries);
}
