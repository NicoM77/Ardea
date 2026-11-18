using Ardea.Intel.Features;
using Ardea.Intel.Sources.Abstractions;
using Ardea.TestSupport;

namespace Ardea.Intel.Tests;

/// <summary>Completeness check and maintenance commands (task 1.11; master plan 6.8, 12.8).</summary>
public sealed class MaintenanceTests(PostgresServer server)
{
    private static readonly DateTimeOffset Old = new(2026, 11, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset New = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_change_log_with_gaps_and_duplicates_leaves_no_record_missing_after_the_check()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        host.Provider.Items.AddRange([
            ScriptedProvider.Json("A", """{"id":"A"}""") with { ModifiedAt = Old },
            ScriptedProvider.Json("B", """{"id":"B","v":1}""") with { ModifiedAt = Old },
            new Checkpoint("c1"),
        ]);
        await host.SyncAsync();

        // The log knows a newer B and two records no delta ever delivered; C appears twice.
        host.Provider.ChangeLog.AddRange([
            new ChangedRecord("A", Old),
            new ChangedRecord("B", New),
            new ChangedRecord("C", Old),
            new ChangedRecord("C", New),
            new ChangedRecord("D", null),
            new ChangedRecord("Z", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)),
        ]);
        host.Provider.Singles["B"] = ScriptedProvider.Json("B", """{"id":"B","v":2}""") with { ModifiedAt = New };
        host.Provider.Singles["C"] = ScriptedProvider.Json("C", """{"id":"C"}""") with { ModifiedAt = New };
        host.Provider.Singles["D"] = ScriptedProvider.Json("D", """{"id":"D"}""");

        await host.CheckCompletenessAsync();

        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'intel.sync-source' AND state = 'pending'"));
        Assert.Equal("""["B", "C", "D"]""", await host.ScalarAsync<string>(
            "SELECT (payload -> 'singleIds')::text FROM platform.job WHERE kind = 'intel.sync-source' AND state = 'pending'"));

        await host.RunJobsAsync("intel.sync-source");

        Assert.Equal("A,B,C,D", await host.ScalarAsync<string>("SELECT string_agg(external_id, ',' ORDER BY external_id) FROM intel.source_record"));
        Assert.Equal(2, await host.ScalarAsync<int>("SELECT revision FROM intel.source_record WHERE external_id = 'B'"));
        Assert.Equal("succeeded", await host.ScalarAsync<string>("SELECT outcome FROM intel.source_run ORDER BY id DESC LIMIT 1"));
        Assert.Equal("c1", await host.ScalarAsync<string>("SELECT cursor FROM intel.source WHERE key = 'scripted'"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'intel.normalize-records' AND state = 'pending'"));

        // A second check finds nothing left to fetch.
        await host.CheckCompletenessAsync();
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'intel.sync-source' AND state = 'pending'"));
    }

    [Fact]
    public async Task A_single_record_that_fails_is_reported_and_the_others_are_stored()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        host.Provider.ChangeLog.AddRange([new ChangedRecord("A", New), new ChangedRecord("GONE", New)]);
        host.Provider.Singles["A"] = ScriptedProvider.Json("A", """{"id":"A"}""") with { ModifiedAt = New };

        await host.CheckCompletenessAsync();
        await host.RunJobsAsync("intel.sync-source");

        Assert.Equal("A", await host.ScalarAsync<string>("SELECT string_agg(external_id, ',') FROM intel.source_record"));
        Assert.Equal("failed", await host.ScalarAsync<string>("SELECT outcome FROM intel.source_run ORDER BY id DESC LIMIT 1"));
        Assert.Contains("GONE", await host.ScalarAsync<string>("SELECT error FROM intel.source_run ORDER BY id DESC LIMIT 1"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renormalize_reads_every_stored_record_of_the_source_again()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        host.Provider.Items.AddRange([ScriptedProvider.Json("A", """{"id":"A"}"""), ScriptedProvider.Json("B", """{"id":"B"}"""), new Checkpoint("c1")]);
        await host.SyncAsync();
        await host.NormalizeAsync();
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.source_record WHERE parse_status = 'pending'"));
        await host.ExecuteAsync("UPDATE platform.job SET state = 'succeeded' WHERE state = 'pending'");

        Assert.Equal(64, await host.RunCommandAsync(IntelCommands.Renormalize, "--source", "nope"));
        Assert.Equal(0, await host.RunCommandAsync(IntelCommands.Renormalize, "--source", ScriptedProvider.Key));

        Assert.Equal(2L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.source_record WHERE parse_status = 'pending'"));
        await host.RunJobsAsync("intel.normalize-records");
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.source_record WHERE parse_status = 'pending'"));
    }

    [Fact]
    public async Task Recompute_merges_every_entry_again()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        host.Provider.Items.AddRange([ScriptedProvider.Json("A", """{"id":"A","title":"First"}"""), ScriptedProvider.Json("B", """{"id":"B"}"""), ScriptedProvider.Json("W", """{"id":"W","kind":"warning","title":"Warn"}"""), new Checkpoint("c1")]);
        await host.SyncAsync();
        await host.RunJobsAsync("intel.normalize-records", "intel.correlate-entry");
        Assert.Equal(3L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry"));

        // A rule change the stored entry does not reflect yet.
        await host.ExecuteAsync("UPDATE intel.security_entry SET title = 'stale' WHERE title IN ('First', 'Warn')");

        Assert.Equal(0, await host.RunCommandAsync(IntelCommands.Recompute));
        Assert.Equal(3L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'intel.correlate-entry' AND state = 'pending'"));
        await host.RunJobsAsync("intel.correlate-entry");

        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry WHERE title = 'stale'"));
        Assert.Equal(3L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry"));
    }
}
