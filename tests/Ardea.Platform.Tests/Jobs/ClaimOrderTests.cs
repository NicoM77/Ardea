using Ardea.Platform.Jobs;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Tests.Jobs;

public sealed class ClaimOrderTests(PostgresServer server)
{
    [Fact]
    public async Task A_claim_hands_the_jobs_out_in_run_at_order()
    {
        const int total = 300;
        await using var host = await JobTestHost.CreateAsync(server, s => s.AddJob<EffectJob, EffectJobHandler>());
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        await host.InUnitOfWorkAsync(async services =>
        {
            var queue = services.GetRequiredService<IJobQueue>();
            for (var i = total - 1; i >= 0; i--)
            {
                await queue.EnqueueAsync(new EffectJob($"job-{i:D3}"), new EnqueueOptions { RunAt = start.AddSeconds(i) });
            }
        });

        var claimed = await JobStore.ClaimAsync(host.DataSource, "worker-a", EffectJob.Kind, total, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(
            Enumerable.Range(0, total).Select(i => $"job-{i:D3}"),
            claimed.Select(j => j.Payload.RootElement.GetProperty("ref").GetString()));
    }
}
