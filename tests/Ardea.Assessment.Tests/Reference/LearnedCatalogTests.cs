using System.Text.Json;
using Ardea.Catalog.Learning;
using Ardea.Catalog.Persistence;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Platform.Jobs;
using Ardea.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assessment.Tests.Reference;

/// <summary>
/// The catalogue that learns through the whole pipeline (master plan 6.9; tasks 4.9, 4.10): records name a product the
/// catalogue does not know; the rules learn it; the entries are computed again; a component recorded under the learned
/// key gets a hit that says it came through a learned entry; the free entry that matched the raw key keeps its hit. The
/// withdrawal takes the learned hit back and leaves the other. Without a provider: the rules alone learn.
/// </summary>
public sealed class LearnedCatalogTests(PostgresServer server)
{
    [Fact]
    public async Task A_learned_entry_adds_a_hit_that_names_it_and_removes_none()
    {
        await using var host = await ReferenceHost.StartAsync(server);
        var organization = Guid.CreateVersion7();
        var project = await host.AddProjectAsync(organization, "Portal", new ReferenceProject("normal", "internet"));
        var free = await host.AddComponentAsync(organization, project, c => (c.CustomVendor, c.CustomName, c.Kind, c.Version) = ("Acme", "Widget Server", "service", "2.1"));
        var keyed = await host.AddComponentAsync(organization, project, c => (c.ProductKey, c.Kind, c.Version) = ("widget-server", "service", "2.1"));
        await host.ProfileChangedAsync(organization, project);
        await host.RunJobsAsync();

        await host.StoreRecordsAsync([Record("CVE-2026-990001"), Record("CVE-2026-990002")]);
        await host.RunJobsAsync();

        // Before: the free entry is hit through the raw key; the component under the not yet known key is not.
        Assert.Equal(["acme/widgetserver\t-"], await host.RowsAsync("SELECT DISTINCT product_key, coalesce(learned_from, '-') FROM intel.entry_affected"));
        var before = await Findings(host);
        Assert.Equal([$"{free}\topen\tproduct\t-", $"{free}\topen\tproduct\t-"], before);

        // The catalogue learns: an unknown CPE pair makes a new entry; the entries are merged again and assessed.
        await host.InSystemAsync(s => s.GetRequiredService<IJobQueue>().EnqueueAsync(new GrowCatalog(), GrowCatalog.Options));
        await host.RunJobsAsync();

        Assert.Equal(["widget-server\tacme/widgetserver"], await host.RowsAsync("SELECT DISTINCT product_key, coalesce(learned_from, '-') FROM intel.entry_affected"));
        Assert.Equal(
            [.. before, $"{keyed}\topen\tproduct\twidget-server", $"{keyed}\topen\tproduct\twidget-server"],
            await Findings(host));
        Assert.Equal(["acme/widgetserver"], await host.RowsAsync("""SELECT DISTINCT jsonb_array_elements_text(evidence -> 'learned' -> 'rawKeys') FROM assess.finding WHERE evidence ? 'learned'"""));

        // Withdrawn with one click: the hit through the learned entry closes, its evidence kept as history; the free entry's
        // hit stays as it was.
        await host.InSystemAsync(async s =>
        {
            var db = s.GetRequiredService<CatalogDbContext>();
            var row = await db.UnknownProducts.SingleAsync(u => u.RawKey == "acme/widgetserver");
            Assert.Null(await s.GetRequiredService<LearnedEntries>().WithdrawAsync(row, null, TestContext.Current.CancellationToken));
        });
        host.InvalidateCatalog();
        await host.RunJobsAsync();

        Assert.Equal(["acme/widgetserver\t-"], await host.RowsAsync("SELECT DISTINCT product_key, coalesce(learned_from, '-') FROM intel.entry_affected"));
        Assert.Equal(
            [.. before, $"{keyed}\tnot_affected\tproduct\twidget-server", $"{keyed}\tnot_affected\tproduct\twidget-server"],
            await Findings(host));
    }

    /// <summary>Component, status, match and the learned entry it came through, per finding.</summary>
    private static Task<IReadOnlyList<string>> Findings(ReferenceHost host) => host.RowsAsync("""
        SELECT component_id, status, match_type, coalesce(evidence -> 'learned' ->> 'productKey', '-')
        FROM assess.finding ORDER BY (evidence ? 'learned'), component_id, entry_id
        """);

    private static RawRecord Record(string id) => new(id, JsonSerializer.Serialize(new
    {
        dataType = "CVE_RECORD",
        dataVersion = "5.1",
        cveMetadata = new
        {
            cveId = id,
            assignerOrgId = "00000000-0000-4000-8000-000000000001",
            assignerShortName = "Test",
            state = "PUBLISHED",
            datePublished = "2026-11-15T10:00:00Z",
            dateUpdated = "2026-11-15T10:00:00Z",
        },
        containers = new
        {
            cna = new
            {
                providerMetadata = new { orgId = "00000000-0000-4000-8000-000000000001", shortName = "Test" },
                title = "Widget Server: missing authentication",
                affected = new[]
                {
                    new
                    {
                        vendor = "Acme",
                        product = "Widget Server",
                        cpes = new[] { "cpe:2.3:a:acme:widget_server:*:*:*:*:*:*:*:*" },
                        defaultStatus = "unaffected",
                        versions = new[] { new { version = "2.0", lessThan = "2.5", status = "affected", versionType = "semver" } },
                    },
                },
                descriptions = new[] { new { lang = "en", value = "A missing authentication check in Acme Widget Server before 2.5 lets anyone in." } },
                metrics = new[] { new { cvssV3_1 = new { version = "3.1", baseScore = 9.8, baseSeverity = "CRITICAL", vectorString = "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H" } } },
            },
        },
    }), "application/json");
}
