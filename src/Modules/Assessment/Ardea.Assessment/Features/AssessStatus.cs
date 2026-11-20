using Ardea.Assessment.Persistence;
using Ardea.Platform.Status;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Features;

internal sealed class AssessStatusContributor(AssessDbContext db, TimeProvider time) : IInstanceStatusContributor
{
    public async Task ContributeAsync(InstanceStatusBuilder status, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow().AddDays(-1);
        var usage = await db.Database
            .SqlQuery<ModelUsage>($"SELECT provider, calls, tokens_in, tokens_out, refused FROM assess.model_usage_since({since})")
            .ToListAsync(cancellationToken);
        foreach (var provider in usage.OrderBy(u => u.Provider, StringComparer.Ordinal))
        {
            status.Metrics.Add(new StatusMetric("ai", $"{provider.Provider}.calls", provider.Calls));
            status.Metrics.Add(new StatusMetric("ai", $"{provider.Provider}.tokens_in", provider.TokensIn));
            status.Metrics.Add(new StatusMetric("ai", $"{provider.Provider}.tokens_out", provider.TokensOut));
            status.Metrics.Add(new StatusMetric("ai", $"{provider.Provider}.refused", provider.Refused));
        }
    }

    private sealed record ModelUsage(string Provider, long Calls, long TokensIn, long TokensOut, long Refused);
}
