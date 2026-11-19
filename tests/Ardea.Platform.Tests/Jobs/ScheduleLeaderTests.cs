using Ardea.Platform.Jobs;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Ardea.Platform.Tests.Jobs;

public sealed class ScheduleLeaderTests(PostgresServer server)
{
    [Fact(Timeout = JobTestHost.TestTimeout)]
    public async Task Due_schedule_enqueues_one_run_and_moves_its_next_time()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 10, 0, 30, TimeSpan.Zero));
        await using var host = await JobTestHost.CreateAsync(server, s =>
        {
            s.AddJob<EffectJob, EffectJobHandler>();
            s.AddSchedule("test.every-minute", "* * * * *", new EffectJob("scheduled"));
        });
        var leader = new ScheduleLeader(
            host.DataSource,
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            host.Services.GetServices<ScheduleDefinition>(),
            clock,
            NullLogger<ScheduleLeader>.Instance);
        var ct = TestContext.Current.CancellationToken;

        await leader.SyncDefinitionsAsync(ct);
        await leader.RunDueAsync(ct);
        Assert.Equal(0, await host.CountAsync("SELECT count(*) FROM platform.job WHERE kind = 'test.effect'"));

        clock.Advance(TimeSpan.FromMinutes(1));
        await leader.RunDueAsync(ct);
        await leader.RunDueAsync(ct);

        Assert.Equal(1, await host.CountAsync("SELECT count(*) FROM platform.job WHERE kind = 'test.effect' AND coalesce_key = 'schedule:test.every-minute'"));
        Assert.Equal(1, await host.CountAsync("SELECT count(*) FROM platform.schedule WHERE key = 'test.every-minute' AND next_run_at = '2026-11-18T10:02:00Z'"));
    }
}
