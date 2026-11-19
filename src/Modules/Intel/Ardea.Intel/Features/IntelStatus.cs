using Ardea.Intel.Persistence;
using Ardea.Platform.Status;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Intel.Features;

/// <summary>Per source: lag since the last success, failure streak and quarantine (master plan 12.7).</summary>
internal sealed class IntelStatusContributor(IntelDbContext db, TimeProvider time) : IInstanceStatusContributor
{
    /// <summary>A source not fetched for this long makes the system warn about itself (master plan 12.7).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

    public async Task ContributeAsync(InstanceStatusBuilder status, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var quarantined = await db.SourceRecords.AsNoTracking().Where(r => r.ParseStatus == "quarantined")
            .GroupBy(r => r.SourceId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        foreach (var source in await db.Sources.AsNoTracking().Where(s => s.Enabled).OrderBy(s => s.Key).ToListAsync(cancellationToken))
        {
            var lag = source.LastSuccessAt is { } success ? now - success : (TimeSpan?)null;
            status.Metrics.Add(new StatusMetric("sources", $"{source.Key}.failures", source.ConsecutiveFailures));
            status.Metrics.Add(new StatusMetric("sources", $"{source.Key}.quarantined", quarantined.GetValueOrDefault(source.Id)));
            if (lag is { } l)
            {
                status.Metrics.Add(new StatusMetric("sources", $"{source.Key}.lag", Math.Round(l.TotalMinutes), "min"));
            }

            // Stale: no success for six hours, or none at all six hours after the source was registered.
            var since = source.LastSuccessAt ?? source.CreatedAt;
            if (now - since > StaleAfter)
            {
                status.Warnings.Add(new StatusWarning("source_stale", "warning",
                    $"{source.Name} has not been fetched since {(source.LastSuccessAt is null ? "it was added" : source.LastSuccessAt.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture))}.",
                    source.LastSuccessAt, source.Key));
            }
        }
    }
}
