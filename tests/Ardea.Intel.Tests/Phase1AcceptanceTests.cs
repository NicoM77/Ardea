using System.Runtime.CompilerServices;
using Ardea.Intel.Sources.Abstractions;
using Ardea.TestSupport;

namespace Ardea.Intel.Tests;

public sealed class Phase1AcceptanceTests(PostgresServer server)
{
    [Fact]
    public async Task One_cve_from_every_source_is_one_entry_with_provenance_and_the_national_mark()
    {
        var authority = new AuthorityProvider();
        await using var host = await IntelTestHost.StartAsync(server, authority);

        host.Provider.Items.AddRange([ScriptedProvider.Json("CVE-2026-4242", """{"aliases":["CVE:CVE-2026-4242"],"title":"Remote code execution in Acme Portal","score":9.8}"""), new Checkpoint("c1")]);
        await host.SyncAsync();
        host.Signals.Signals.AddRange([new Signal("CVE", "CVE-2026-4242") { KevListedOn = new DateOnly(2026, 11, 18), KevUrl = new Uri("https://www.cisa.gov/kev"), EpssScore = 0.91, EpssPercentile = 0.99 }]);
        await host.SyncAsync(ScriptedSignalProvider.Key);
        await host.RunJobsAsync("intel.normalize-records", "intel.correlate-entry");

        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry"));
        Assert.Equal("active|high|false", await host.ScalarAsync<string>("SELECT exploitation || '|' || epss_class || '|' || authority_warning FROM intel.security_entry"));
        var provenance = await host.ScalarAsync<string>("SELECT provenance::text FROM intel.security_entry");
        Assert.Contains("\"title\": \"scripted\"", provenance, StringComparison.Ordinal);
        Assert.Contains("\"exploitation\": \"cisa-kev\"", provenance, StringComparison.Ordinal);
        Assert.Contains("\"epss\": \"first-epss\"", provenance, StringComparison.Ordinal);

        authority.Items.AddRange([AuthorityProvider.Advisory("hub-1", "Acme Portal: patch now", null), new Checkpoint("a1")]);
        await host.SyncAsync(AuthorityProvider.Key);
        await host.RunJobsAsync("intel.normalize-records", "intel.correlate-entry");
        Assert.False(await host.ScalarAsync<bool>("SELECT authority_warning FROM intel.security_entry WHERE primary_key = 'CVE-2026-4242'"));

        authority.Items.Clear();
        authority.Items.AddRange([AuthorityProvider.Advisory("hub-1", "Acme Portal: patch now", "CVE-2026-4242"), new Checkpoint("a2")]);
        await host.SyncAsync(AuthorityProvider.Key);
        await host.RunJobsAsync("intel.normalize-records", "intel.correlate-entry");

        Assert.True(await host.ScalarAsync<bool>("SELECT authority_warning FROM intel.security_entry WHERE primary_key = 'CVE-2026-4242'"));
        Assert.Equal(1L, await host.ScalarAsync<long>(
            "SELECT count(*) FROM intel.entry_source es JOIN intel.source s ON s.id = es.source_id JOIN intel.security_entry e ON e.id = es.entry_id WHERE s.key = 'scripted-authority' AND e.primary_key = 'CVE-2026-4242'"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry WHERE kind <> 'warning' AND state <> 'merged'"));
    }

    private sealed class AuthorityProvider : ISourceProvider
    {
        public const string Key = "scripted-authority";

        public SourceDescriptor Descriptor { get; } = new()
        {
            Key = Key,
            Name = "Scripted national authority",
            Kind = SourceKind.Record,
            Operator = "Tests",
            OperatorKind = OperatorKind.Authority,
            DefaultInterval = TimeSpan.FromHours(1),
            AllowedHosts = ["example.org"],
            Attribution = "Test data",
        };

        public List<FetchItem> Items { get; } = [];

        public static RawRecord Advisory(string id, string title, string? cve) =>
            new(id, System.Text.Json.JsonSerializer.Serialize(new { id, title, cve }), "application/json");

        public async IAsyncEnumerable<FetchItem> FetchAsync(SyncContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in Items)
            {
                await Task.Yield();
                yield return item;
            }
        }

        public NormalizationResult Normalize(RawRecord record)
        {
            using var document = System.Text.Json.JsonDocument.Parse(record.Content);
            var title = document.RootElement.GetProperty("title").GetString();
            var cve = document.RootElement.GetProperty("cve").GetString();
            return NormalizationResult.Success(new Observation
            {
                Kind = cve is null ? ObservationKind.Warning : ObservationKind.Advisory,
                Title = title,
                Claims = cve is null ? [] : [new ClaimBlock { Aliases = [new Identifier("CVE", cve)], Title = title }],
            });
        }
    }
}
