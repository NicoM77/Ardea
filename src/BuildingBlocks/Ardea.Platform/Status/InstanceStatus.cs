using Ardea.Platform.Hosting;
using Ardea.Platform.Persistence;
using Ardea.Platform.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Platform.Status;

/// <summary>
/// The system warns about itself when it no longer works reliably (master plan 12.7): a source not
/// fetched for six hours, a stalled worker, failing deliveries. Users see the warning on the overview;
/// the instance administrator also sees the operating figures.
/// </summary>
public sealed record StatusWarning(string Code, string Severity, string Message, DateTimeOffset? Since = null, string? Subject = null);

/// <summary>One figure of the operation, e.g. jobs waiting of a kind; counts only, never contents (master plan 9.2).</summary>
public sealed record StatusMetric(string Group, string Name, double Value, string? Unit = null);

public sealed class InstanceStatusBuilder
{
    public List<StatusWarning> Warnings { get; } = [];

    public List<StatusMetric> Metrics { get; } = [];
}

/// <summary>A module's part of the instance status; runs in a unit of work without an organisation.</summary>
public interface IInstanceStatusContributor
{
    Task ContributeAsync(InstanceStatusBuilder status, CancellationToken cancellationToken);
}

/// <summary>Worker and job queue.</summary>
internal sealed class PlatformStatusContributor(PlatformDbContext db, TimeProvider time) : IInstanceStatusContributor
{
    public async Task ContributeAsync(InstanceStatusBuilder status, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var beats = await db.WorkerHeartbeats.AsNoTracking().Select(b => b.BeatAt).ToListAsync(cancellationToken);
        var newest = beats.Count == 0 ? (DateTimeOffset?)null : beats.Max();
        status.Metrics.Add(new StatusMetric("worker", "alive", beats.Count(b => now - b < WorkerHeartbeat.MaxAge)));
        if (newest is null || now - newest > WorkerHeartbeat.MaxAge)
        {
            status.Warnings.Add(new StatusWarning("worker_stalled", "critical",
                newest is null ? "No worker has started yet: nothing is fetched or assessed." : "The worker has stopped: nothing is fetched or assessed.", newest));
        }

        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => j.State == JobState.Pending || j.State == JobState.Running || ((j.State == JobState.Failed || j.State == JobState.Dead) && j.FinishedAt > now.AddDays(-1)))
            .GroupBy(j => new { j.Kind, j.State })
            .Select(g => new { g.Key.Kind, g.Key.State, Count = g.Count(), Oldest = g.Min(j => j.RunAt) })
            .ToListAsync(cancellationToken);
        foreach (var group in jobs.OrderBy(j => j.Kind, StringComparer.Ordinal))
        {
            status.Metrics.Add(new StatusMetric("jobs", $"{group.Kind}.{group.State.ToString().ToLowerInvariant()}", group.Count));
            if (group.State == JobState.Pending)
            {
                status.Metrics.Add(new StatusMetric("jobs", $"{group.Kind}.oldest_wait", Math.Max(0, (now - group.Oldest).TotalSeconds), "s"));
            }
        }
    }
}
