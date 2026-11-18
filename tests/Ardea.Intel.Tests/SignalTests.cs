using Ardea.Intel.Sources.Abstractions;
using Ardea.TestSupport;

namespace Ardea.Intel.Tests;

/// <summary>KEV and EPSS as signals per identifier (task 1.8, master plan 4.4, 6.4).</summary>
public sealed class SignalTests(PostgresServer server)
{
    [Fact]
    public async Task Kev_listing_makes_the_entry_actively_exploited_with_evidence()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        await DeliverAsync(host, ("A", """{"aliases":["CVE:CVE-2026-1"],"score":7.5}"""));

        host.Signals.Signals.Add(new Signal("CVE", "CVE-2026-1") { KevListedOn = new DateOnly(2026, 11, 18), KevUrl = new Uri("https://www.cisa.gov/kev?cve=CVE-2026-1") });
        await host.SyncAsync(ScriptedSignalProvider.Key);
        await host.RunJobsAsync("intel.correlate-entry");

        Assert.Equal("active", await host.ScalarAsync<string>("SELECT exploitation FROM intel.security_entry"));
        Assert.Contains("cisa-kev", await host.ScalarAsync<string>("SELECT exploitation_evidence::text FROM intel.security_entry"), StringComparison.Ordinal);
        Assert.Equal("exploitation", await host.ScalarAsync<string>("SELECT change_kinds[1] FROM intel.entry_change WHERE revision = 2"));
        Assert.Equal("scripted-cursor", await host.ScalarAsync<string>("SELECT cursor FROM intel.source WHERE key = 'scripted-signals'"));
    }

    [Fact]
    public async Task Daily_epss_run_touches_only_entries_whose_class_changes()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        await DeliverAsync(host, ("A", """{"aliases":["CVE:CVE-2026-1"]}"""), ("B", """{"aliases":["CVE:CVE-2026-2"]}"""), ("C", """{"aliases":["CVE:CVE-2026-3"]}"""));
        await EpssAsync(host, ("CVE-2026-1", 0.70), ("CVE-2026-2", 0.10), ("CVE-2026-3", 0.20), ("CVE-2026-999", 0.90));
        Assert.Equal("high", await host.ScalarAsync<string>("SELECT epss_class FROM intel.security_entry WHERE primary_key = 'CVE-2026-1'"));

        // Next day: one entry crosses clearly, one stays inside the band, one barely moves.
        var revisions = await host.ScalarAsync<long>("SELECT sum(revision) FROM intel.security_entry");
        await EpssAsync(host, ("CVE-2026-1", 0.52), ("CVE-2026-2", 0.56), ("CVE-2026-3", 0.21), ("CVE-2026-999", 0.95));

        Assert.Equal(revisions + 1, await host.ScalarAsync<long>("SELECT sum(revision) FROM intel.security_entry"));
        Assert.Equal("high", await host.ScalarAsync<string>("SELECT epss_class FROM intel.security_entry WHERE primary_key = 'CVE-2026-1'"));
        Assert.Equal("high", await host.ScalarAsync<string>("SELECT epss_class FROM intel.security_entry WHERE primary_key = 'CVE-2026-2'"));
        Assert.Equal("epss", await host.ScalarAsync<string>(
            "SELECT c.change_kinds[1] FROM intel.entry_change c JOIN intel.security_entry e ON e.id = c.entry_id WHERE e.primary_key = 'CVE-2026-2' ORDER BY c.revision DESC LIMIT 1"));
        // A signal for an identifier without entry is kept for later and creates nothing.
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry WHERE primary_key = 'CVE-2026-999'"));
        Assert.Equal(0.95, await host.ScalarAsync<double>("SELECT epss_score FROM intel.entry_signal WHERE value = 'CVE-2026-999'"));
    }

    [Fact]
    public async Task Epss_values_leave_the_kev_listing_alone()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        host.Signals.Signals.Add(new Signal("CVE", "CVE-2026-5") { KevListedOn = new DateOnly(2026, 1, 2) });
        await host.SyncAsync(ScriptedSignalProvider.Key);

        await EpssAsync(host, ("CVE-2026-5", 0.3));

        Assert.Equal(new DateOnly(2026, 1, 2), await host.ScalarAsync<DateOnly>("SELECT kev_listed_on FROM intel.entry_signal"));
    }

    private static async Task EpssAsync(IntelTestHost host, params (string Cve, double Score)[] scores)
    {
        host.Signals.Signals.Clear();
        host.Signals.Signals.AddRange(scores.Select(s => new Signal("CVE", s.Cve) { EpssScore = s.Score, EpssPercentile = 0.5 }));
        await host.SyncAsync(ScriptedSignalProvider.Key);
        await host.RunJobsAsync("intel.correlate-entry");
    }

    private static async Task DeliverAsync(IntelTestHost host, params (string Id, string Json)[] records)
    {
        host.Provider.Items.Clear();
        host.Provider.Items.AddRange(records.Select(r => (FetchItem)ScriptedProvider.Json(r.Id, r.Json)));
        host.Provider.Items.Add(new Checkpoint(Guid.NewGuid().ToString()));
        await host.SyncAsync();
        await host.RunJobsAsync("intel.normalize-records", "intel.correlate-entry");
    }
}
