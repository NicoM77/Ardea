using System.Runtime.CompilerServices;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sources.CveProgram;
using Ardea.Intel.Sources.Osv;
using Ardea.TestSupport;

namespace Ardea.Intel.Tests;

public sealed class OsvMergeTests(PostgresServer server)
{
    private static readonly string[] Pipeline = ["intel.normalize-records", "intel.correlate-entry"];

    [Fact]
    public async Task A_ghsa_record_that_later_names_its_cve_merges_into_the_cve_entry_and_its_key_redirects()
    {
        var osv = new Replay(new OsvProvider());
        var cve = new Replay(new CveProgramProvider());
        await using var host = await IntelTestHost.StartAsync(server, osv, cve);

        await DeliverAsync(host, osv, "GHSA-pfrf-9r5f-73f5", "Go.GHSA-pfrf-9r5f-73f5.ghsa-only.json");
        await DeliverAsync(host, cve, "CVE-2026-29067", "CVE-2026-29067.json");
        Assert.Equal(2L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.source_record WHERE parse_status <> 'parsed'"));

        await DeliverAsync(host, osv, "GHSA-pfrf-9r5f-73f5", "Go.GHSA-pfrf-9r5f-73f5.json");

        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry WHERE state <> 'merged'"));
        Assert.Equal("merged", await host.ScalarAsync<string>("SELECT state FROM intel.security_entry WHERE primary_key = 'GHSA-pfrf-9r5f-73f5'"));
        Assert.True(await host.ScalarAsync<bool>(
            "SELECT m.merged_into_id = w.id FROM intel.security_entry m, intel.security_entry w WHERE m.primary_key = 'GHSA-pfrf-9r5f-73f5' AND w.primary_key = 'CVE-2026-29067'"));
        Assert.Equal(3L, await host.ScalarAsync<long>(
            "SELECT count(*) FROM intel.entry_alias a JOIN intel.security_entry e ON e.id = a.entry_id WHERE e.primary_key = 'CVE-2026-29067'"));
        Assert.True(await host.ScalarAsync<bool>(
            "SELECT count(*) > 0 FROM intel.entry_affected a JOIN intel.security_entry e ON e.id = a.entry_id WHERE e.primary_key = 'CVE-2026-29067' AND a.purl_type = 'golang' AND a.purl_name = 'github.com/zitadel/zitadel'"));
    }

    private static async Task DeliverAsync(IntelTestHost host, Replay source, string id, string file)
    {
        source.Items.Clear();
        source.Items.Add(new RawRecord(id, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", file)), "application/json"));
        source.Items.Add(new Checkpoint(Guid.NewGuid().ToString()));
        await host.SyncAsync(source.Descriptor.Key);
        await host.RunJobsAsync(Pipeline);
    }

    private sealed class Replay(ISourceProvider adapter) : ISourceProvider
    {
        public List<FetchItem> Items { get; } = [];

        public SourceDescriptor Descriptor => adapter.Descriptor;

        public NormalizationResult Normalize(RawRecord record) => adapter.Normalize(record);

        public async IAsyncEnumerable<FetchItem> FetchAsync(SyncContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in Items)
            {
                await Task.Yield();
                yield return item;
            }
        }
    }
}
