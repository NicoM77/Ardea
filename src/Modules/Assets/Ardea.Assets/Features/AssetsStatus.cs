using Ardea.Assets.Persistence;
using Ardea.Platform.Status;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assets.Features;

internal sealed class AssetsStatusContributor(AssetsDbContext db) : IInstanceStatusContributor
{
    public async Task ContributeAsync(InstanceStatusBuilder status, CancellationToken cancellationToken)
    {
        var counts = await db.Database
            .SqlQuery<LinkCount>($"SELECT state AS \"State\", links AS \"Links\" FROM assets.repository_link_counts()")
            .ToListAsync(cancellationToken);
        foreach (var count in counts.OrderBy(c => c.State, StringComparer.Ordinal))
        {
            status.Metrics.Add(new StatusMetric("repositories", count.State, count.Links));
        }
    }

    private sealed record LinkCount(string State, long Links);
}
