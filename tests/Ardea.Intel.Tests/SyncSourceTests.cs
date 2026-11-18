using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sync;
using Ardea.TestSupport;

namespace Ardea.Intel.Tests;

/// <summary>Sync control and raw record storage (tasks 1.2, part of 1.5; master plan 5.2, 5.4, 6.4).</summary>
public sealed class SyncSourceTests(PostgresServer server)
{
    [Fact]
    public async Task Records_and_cursor_commit_at_a_checkpoint_and_a_failed_run_resumes_there()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        host.Provider.Items.AddRange([
            ScriptedProvider.Json("A", """{"id":"A"}"""),
            ScriptedProvider.Json("B", """{"id":"B"}"""),
            new Checkpoint("cursor-1"),
            ScriptedProvider.Json("C", """{"id":"C"}"""),
            new Checkpoint("cursor-2"),
        ]);
        host.Provider.FailAfter = 4;

        await host.SyncAsync();

        Assert.Equal("cursor-1", await host.ScalarAsync<string>("SELECT cursor FROM intel.source"));
        Assert.Equal(2L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.source_record"));
        Assert.Equal("failed", await host.ScalarAsync<string>("SELECT outcome FROM intel.source_run"));

        host.Provider.FailAfter = null;
        host.Clock.Advance(TimeSpan.FromHours(2));
        await host.SyncAsync();

        Assert.Equal("cursor-1", host.Provider.CursorSeen);
        Assert.Equal(SyncMode.Incremental, host.Provider.ModeSeen);
        Assert.Equal("cursor-2", await host.ScalarAsync<string>("SELECT cursor FROM intel.source"));
        Assert.Equal(3L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.source_record"));
    }

    [Fact]
    public async Task The_same_delta_twice_creates_no_revision_even_when_reformatted()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        host.Provider.Items.AddRange([ScriptedProvider.Json("A", """{"id":"A","v":1}"""), new Checkpoint("c1")]);
        await host.SyncAsync();

        host.Provider.Items.Clear();
        host.Provider.Items.AddRange([ScriptedProvider.Json("A", """{ "v": 1, "id": "A" }"""), new Checkpoint("c1")]);
        await host.SyncAsync();

        Assert.Equal(1, await host.ScalarAsync<int>("SELECT revision FROM intel.source_record"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.source_record_revision"));
        Assert.Equal(0, await host.ScalarAsync<int>("SELECT changed FROM intel.source_run ORDER BY id DESC LIMIT 1"));
    }

    [Fact]
    public async Task A_changed_record_gets_a_revision_and_the_first_and_newest_ten_remain()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        for (var version = 1; version <= 14; version++)
        {
            host.Provider.Items.Clear();
            host.Provider.Items.AddRange([ScriptedProvider.Json("A", $$"""{"id":"A","v":{{version}}}"""), new Checkpoint($"c{version}")]);
            await host.SyncAsync();
        }

        Assert.Equal(14, await host.ScalarAsync<int>("SELECT revision FROM intel.source_record"));
        Assert.Equal(11L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.source_record_revision"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.source_record_revision WHERE revision = 1"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.source_record_revision WHERE revision BETWEEN 2 AND 4"));
        Assert.Equal("pending", await host.ScalarAsync<string>("SELECT parse_status FROM intel.source_record"));
    }

    [Fact]
    public async Task Failed_runs_back_off_up_to_six_hours_and_turn_the_source_failing()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        host.Provider.Items.Add(ScriptedProvider.Json("A", "{}"));
        host.Provider.FailAfter = 0;

        await host.SyncAsync();
        Assert.Equal("degraded", await host.ScalarAsync<string>("SELECT health FROM intel.source"));
        await host.SyncAsync();
        await host.SyncAsync();

        Assert.Equal("failing", await host.ScalarAsync<string>("SELECT health FROM intel.source"));
        Assert.Equal(3, await host.ScalarAsync<int>("SELECT consecutive_failures FROM intel.source"));
        // The test clock stands still at 12:00: the third failure doubles the hour to eight, capped at six.
        Assert.True(await host.ScalarAsync<bool>(
            "SELECT next_sync_at BETWEEN '2026-11-18T18:00:00Z'::timestamptz AND '2026-11-18T18:10:00Z'::timestamptz FROM intel.source"));

        host.Provider.FailAfter = null;
        await host.SyncAsync();
        Assert.Equal("healthy", await host.ScalarAsync<string>("SELECT health FROM intel.source"));
        Assert.Equal(0, await host.ScalarAsync<int>("SELECT consecutive_failures FROM intel.source"));
    }

    [Fact]
    public async Task First_run_is_a_backfill_from_the_configured_window()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        host.Provider.Items.Add(new Checkpoint("c1"));

        await host.SyncAsync();

        Assert.Equal(SyncMode.Backfill, host.Provider.ModeSeen);
        Assert.Null(host.Provider.CursorSeen);
    }

    [Fact]
    public async Task Due_source_is_enqueued_once_and_pushed_past_its_run()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        host.Clock.Advance(TimeSpan.FromMinutes(5));

        for (var i = 0; i < 2; i++)
        {
            await using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(host.Services);
            var handler = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ScheduleDueSourcesHandler>(scope.ServiceProvider);
            await handler.HandleAsync(new ScheduleDueSources(), new Platform.Jobs.JobContext(Guid.CreateVersion7(), ScheduleDueSources.Kind, 1, null), CancellationToken.None);
            await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Platform.Persistence.IUnitOfWork>(scope.ServiceProvider).CommitAsync(CancellationToken.None);
        }

        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'intel.sync-source'"));
        Assert.True(await host.ScalarAsync<bool>("SELECT next_sync_at > '2026-11-18T13:00:00Z'::timestamptz FROM intel.source"));
    }

    [Fact]
    public async Task Registration_updates_master_data_but_keeps_operator_settings()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        await host.ExecuteAsync("UPDATE intel.source SET enabled = false, interval = interval '3 hours', name = 'stale name'");

        await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetServices<Microsoft.Extensions.Hosting.IHostedService>(host.Services).OfType<SourceRegistrar>().Single().RegisterAsync(CancellationToken.None);

        Assert.Equal("Scripted test source", await host.ScalarAsync<string>("SELECT name FROM intel.source"));
        Assert.False(await host.ScalarAsync<bool>("SELECT enabled FROM intel.source"));
        Assert.Equal(TimeSpan.FromHours(3), await host.ScalarAsync<TimeSpan>("SELECT interval FROM intel.source"));
    }
}
