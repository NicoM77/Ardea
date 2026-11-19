using Ardea.Notifications.Persistence;
using Ardea.Platform.Status;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Notifications.Evaluation;

/// <summary>
/// Events set aside after repeated failures (ADR 038), counted over all organisations through the narrow function
/// <c>notify.failed_event_count</c>: the status runs without an organisation and sees a count only.
/// </summary>
internal sealed class NotifyStatusContributor(NotifyDbContext db) : IInstanceStatusContributor
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
    }
}
