using Ardea.Intel.Sources.Abstractions;
using Ardea.TestSupport;

namespace Ardea.Intel.Tests;

public sealed class NormalizationTests(PostgresServer server)
{
    [Fact]
    public async Task Stored_records_become_observations_and_unreadable_ones_wait_in_quarantine()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        host.Provider.Items.AddRange([
            ScriptedProvider.Json("A", """{"id":"A","title":"First"}"""),
            ScriptedProvider.Json("B", """{"id":"B","bad":true}"""),
            new Checkpoint("c1"),
        ]);
        await host.SyncAsync();
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'intel.normalize-records'"));

        await host.NormalizeAsync();

        Assert.Equal("First", await host.ScalarAsync<string>("SELECT title FROM intel.observation"));
        Assert.Equal("TEST:A", await host.ScalarAsync<string>("SELECT aliases[1] FROM intel.observation"));
        Assert.Equal("scripted bad record", await host.ScalarAsync<string>("SELECT quarantine_reason FROM intel.source_record WHERE external_id = 'B'"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.source_record WHERE parse_status = 'pending'"));
    }

    [Fact]
    public async Task A_new_revision_replaces_the_observation_and_a_repeat_changes_nothing()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        host.Provider.Items.AddRange([ScriptedProvider.Json("A", """{"id":"A","title":"Old"}"""), new Checkpoint("c1")]);
        await host.SyncAsync();
        await host.NormalizeAsync();

        host.Provider.Items.Clear();
        host.Provider.Items.AddRange([ScriptedProvider.Json("A", """{"id":"A","title":"New"}"""), new Checkpoint("c2")]);
        await host.SyncAsync();
        await host.NormalizeAsync();
        await host.NormalizeAsync();

        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.observation"));
        Assert.Equal("New", await host.ScalarAsync<string>("SELECT title FROM intel.observation"));
        Assert.Equal(2, await host.ScalarAsync<int>("SELECT record_revision FROM intel.observation"));
    }

    [Fact]
    public async Task More_than_five_percent_in_quarantine_marks_the_source_degraded()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        for (var i = 0; i < 30; i++)
        {
            host.Provider.Items.Add(ScriptedProvider.Json($"R{i}", i < 3 ? $$"""{"id":"R{{i}}","bad":true}""" : $$"""{"id":"R{{i}}"}"""));
        }

        host.Provider.Items.Add(new Checkpoint("c1"));
        await host.SyncAsync();
        await host.NormalizeAsync();

        Assert.Equal("degraded", await host.ScalarAsync<string>("SELECT health FROM intel.source WHERE key = 'scripted'"));
        Assert.Contains("3 of 30", await host.ScalarAsync<string>("SELECT health_reason FROM intel.source WHERE key = 'scripted'"), StringComparison.Ordinal);
    }
}
