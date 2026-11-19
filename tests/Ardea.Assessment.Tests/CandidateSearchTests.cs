using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assessment.Tests;

/// <summary>
/// Candidate search forward and backward with match strength (task 3.2; master plan 7.2) and the
/// version check with its three outcomes (task 3.3; master plan 6.6, 7.3).
/// </summary>
public sealed class CandidateSearchTests(PostgresServer server)
{
    private const string Findings = """
        SELECT p.name, f.match_type, coalesce(f.version_verdict, '-'), f.verdict, f.level, f.status, coalesce(f.status_reason, '-'), coalesce(f.fixed_version, '-')
        FROM assess.finding f JOIN assets.project p ON p.id = f.project_id JOIN assets.component c ON c.id = f.component_id
        ORDER BY p.name, c.version
        """;

    [Fact]
    public async Task An_entry_reaches_the_organisations_that_carry_its_product_by_key_cpe_and_package()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var other = AssessmentTestHost.NewOrganization();
        var shop = await host.AddProjectAsync(acme, "Shop");
        await host.AddComponentAsync(acme, shop, c => (c.ProductKey, c.Kind, c.Version) = ("postgresql", "database", "16.2"));
        var web = await host.AddProjectAsync(acme, "Web");
        await host.AddComponentAsync(acme, web, c => (c.CustomName, c.Kind, c.Version, c.Purl, c.Origin) = ("lodash", "library", "4.17.20", "pkg:npm/lodash@4.17.20", "repository"));
        var unrelated = await host.AddProjectAsync(other, "Unrelated");
        await host.AddComponentAsync(other, unrelated, c => (c.ProductKey, c.Kind, c.Version) = ("nginx", "network", "1.27.0"));

        var postgres = await host.AddEntryAsync(new TestEntry("CVE-2026-1001", new TestAffected { CpeVendor = "postgresql", CpeProduct = "postgresql", Introduced = "16.0", Fixed = "16.4" }));
        var lodash = await host.AddEntryAsync(new TestEntry("GHSA-aaaa-bbbb-cccc", new TestAffected { PurlType = "npm", PurlName = "lodash", Scheme = "semver", Fixed = "4.17.21" }));
        await host.EntryChangedAsync(postgres);
        await host.EntryChangedAsync(lodash);
        await host.RunJobsAsync();

        Assert.Equal(
            [
                "Shop\tcpe\tin_range\taffected\t3\topen\t-\t16.4",
                "Web\tpackage\tin_range\taffected\t3\topen\t-\t4.17.21",
            ],
            await host.RowsAsync(Findings));
        Assert.Equal(0L, await host.ScalarAsync<long>($"SELECT count(*) FROM assess.alert WHERE organization_id = '{other}'"));
        // The other organisation never got a job: the system operation named only Acme.
        Assert.Equal(0L, await host.ScalarAsync<long>($"SELECT count(*) FROM platform.job WHERE org_id = '{other}'"));
    }

    [Fact]
    public async Task Weak_matches_are_stored_as_level_1_and_not_affected()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var blog = await host.AddProjectAsync(acme, "Blog");
        await host.AddComponentAsync(acme, blog, c => (c.ProductKey, c.Kind, c.Version) = ("wordpress", "cms", "6.7.1"));
        var data = await host.AddProjectAsync(acme, "Data");
        await host.AddComponentAsync(acme, data, c => (c.ProductKey, c.Kind, c.Version) = ("mariadb", "database", "11.4.2"));
        var free = await host.AddProjectAsync(acme, "Free");
        await host.AddComponentAsync(acme, free, c => (c.CustomName, c.Kind, c.Version) = ("Billing Engine", "other", "2.0"));

        // PHP is related to WordPress, MySQL to MariaDB; Yoast SEO is a WordPress plugin not in the profile.
        var entries = new[]
        {
            await host.AddEntryAsync(new TestEntry("CVE-2026-2001", new TestAffected { ProductKey = "php", Fixed = "8.3.14" })),
            await host.AddEntryAsync(new TestEntry("CVE-2026-2002", new TestAffected { ProductKey = "mysql", Fixed = "8.4.3" })),
            await host.AddEntryAsync(new TestEntry("CVE-2026-2003", new TestAffected { ProductKey = "yoast-seo", Fixed = "23.0" })),
            await host.AddEntryAsync(new TestEntry("CVE-2026-2004", new TestAffected { Vendor = "n/a", Product = "Billing Engine", ProductKey = "billingengine", Fixed = "2.1" })),
        };
        foreach (var entry in entries)
        {
            await host.EntryChangedAsync(entry);
        }

        await host.RunJobsAsync();

        Assert.Equal(
            [
                "Blog\tecosystem\t-\tnot_affected\t1\tnot_affected\tweak_match\t-",
                "Blog\tvariant\t-\tnot_affected\t1\tnot_affected\tweak_match\t-",
                "Data\tvariant\t-\tnot_affected\t1\tnot_affected\tweak_match\t-",
                "Free\tname\t-\tnot_affected\t1\tnot_affected\tweak_match\t-",
            ],
            (await host.RowsAsync(Findings)).Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task The_version_check_honours_ranges_default_status_and_distribution_packages()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var project = await host.AddProjectAsync(acme, "Ops");
        foreach (var version in new[] { "16.1", "16.5", "17.0" })
        {
            await host.AddComponentAsync(acme, project, c => (c.ProductKey, c.Kind, c.Version) = ("postgresql", "database", version));
        }

        await host.AddComponentAsync(acme, project, c => (c.ProductKey, c.Kind, c.Version) = ("postgresql", "database", null));
        await host.AddComponentAsync(acme, project, c => (c.ProductKey, c.Kind, c.Version, c.Packaging) = ("postgresql", "database", "15.0", "distribution"));

        // 16.0 up to before 16.4 affected; 17.x said unaffected; everything else: the record's default status, affected.
        var entry = await host.AddEntryAsync(new TestEntry(
            "CVE-2026-3001",
            new TestAffected { ProductKey = "postgresql", Introduced = "16.0", Fixed = "16.4", DefaultStatus = "affected" },
            new TestAffected { ProductKey = "postgresql", Introduced = "17.0", Fixed = "18.0", Status = "unaffected", DefaultStatus = "affected" }));
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();

        Assert.Equal(
            [
                "Ops\tproduct\tunknown\tuncertain\t2\topen\t-\t-",
                "Ops\tproduct\tunknown\tuncertain\t2\topen\t-\t-",
                "Ops\tproduct\tin_range\taffected\t3\topen\t-\t16.4",
                "Ops\tproduct\tin_range\taffected\t3\topen\t-\t-",
                "Ops\tproduct\tout_of_range\tnot_affected\t1\tnot_affected\tout_of_range\t-",
            ],
            await host.RowsAsync(Findings.Replace("ORDER BY p.name, c.version", "ORDER BY p.name, c.version NULLS FIRST", StringComparison.Ordinal)));
        Assert.Equal(
            ["-\tno_installed_version", "15.0\tdistribution_package", "16.1\tin_affected_range", "16.5\tdefault_affected", "17.0\tin_unaffected_range"],
            await host.RowsAsync("SELECT c.version, f.version_reason FROM assess.finding f JOIN assets.component c ON c.id = f.component_id ORDER BY c.version NULLS FIRST"));
    }

    [Fact]
    public async Task Without_default_status_a_version_above_every_range_is_outside_and_below_an_unnamed_bound_unknown()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var project = await host.AddProjectAsync(acme, "Ops");
        foreach (var version in new[] { "1.2.0", "2.5.0", "3.1.0" })
        {
            await host.AddComponentAsync(acme, project, c => (c.CustomVendor, c.CustomName, c.Kind, c.Version) = ("Acme", "Widget", "library", version));
        }

        var entry = await host.AddEntryAsync(new TestEntry("CVE-2026-3002", new TestAffected { ProductKey = "acme/widget", Introduced = "2.0.0", Fixed = "2.6.0" }));
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();

        // 1.2.0 lies below a lower bound the record names itself: outside. 3.1.0 is above every fix: outside (row 5).
        Assert.Equal(
            ["1.2.0\tout_of_range\toutside_named_bounds", "2.5.0\tin_range\tin_affected_range", "3.1.0\tout_of_range\tabove_all_ranges"],
            await host.RowsAsync("SELECT c.version, f.version_verdict, f.version_reason FROM assess.finding f JOIN assets.component c ON c.id = f.component_id ORDER BY c.version"));
    }

    [Fact]
    public async Task The_reverse_search_finds_entries_in_the_lookback_and_a_raised_version_closes_the_finding()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var recent = await host.AddEntryAsync(new TestEntry("CVE-2026-4001", new TestAffected { CpeVendor = "postgresql", CpeProduct = "postgresql", Fixed = "16.4" }));
        await host.AddEntryAsync(new TestEntry("CVE-2025-4002", new TestAffected { CpeVendor = "postgresql", CpeProduct = "postgresql", Fixed = "16.9" })
        {
            ChangedAt = host.Clock.GetUtcNow().AddDays(-200),
        });
        var project = await host.AddProjectAsync(acme, "Ops");
        var component = await host.AddComponentAsync(acme, project, c => (c.ProductKey, c.Kind, c.Version) = ("postgresql", "database", "16.2"));

        await host.ProfileChangedAsync(acme, project);
        await host.RunJobsAsync();
        Assert.Equal(["Ops\tcpe\tin_range\taffected\t3\topen\t-\t16.4"], await host.RowsAsync(Findings));
        Assert.Equal(recent, await host.ScalarAsync<Guid>("SELECT entry_id FROM assess.finding"));

        // The same profile again changes nothing.
        var changedAt = await host.ScalarAsync<DateTime>("SELECT changed_at FROM assess.finding");
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        await host.ProfileChangedAsync(acme, project);
        await host.RunJobsAsync();
        Assert.Equal(changedAt, await host.ScalarAsync<DateTime>("SELECT changed_at FROM assess.finding"));

        await host.ChangeComponentAsync(acme, component, c => c.Version = "16.4");
        await host.ProfileChangedAsync(acme, project);
        await host.RunJobsAsync();
        Assert.Equal(["Ops\tcpe\tout_of_range\tnot_affected\t1\tremediated\tfixed_by_update\t-"], await host.RowsAsync(Findings));
    }

    [Fact]
    public async Task Withdrawn_entries_and_archived_projects_close_their_findings()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var project = await host.AddProjectAsync(acme, "Ops");
        await host.AddComponentAsync(acme, project, c => (c.ProductKey, c.Kind, c.Version) = ("postgresql", "database", "16.2"));
        var second = await host.AddProjectAsync(acme, "Reports");
        await host.AddComponentAsync(acme, second, c => (c.ProductKey, c.Kind, c.Version) = ("postgresql", "database", "16.1"));
        var entry = await host.AddEntryAsync(new TestEntry("CVE-2026-5001", new TestAffected { ProductKey = "postgresql", Fixed = "16.4" }));
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        Assert.Equal(2L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.finding WHERE status = 'open'"));

        await host.InOrganizationAsync(acme, async services =>
        {
            var db = services.GetRequiredService<Assets.Persistence.AssetsDbContext>();
            db.Projects.Single(p => p.Id == second).ArchivedAt = host.Clock.GetUtcNow();
            await db.SaveChangesAsync();
        });
        await host.ProfileChangedAsync(acme, second);
        await host.RunJobsAsync();
        Assert.Equal(["Reports\tremediated\tproject_archived"], await host.RowsAsync(
            "SELECT p.name, f.status, f.status_reason FROM assess.finding f JOIN assets.project p ON p.id = f.project_id WHERE f.status <> 'open'"));

        await host.ScalarAsync<int>($"UPDATE intel.security_entry SET state = 'rejected' WHERE id = '{entry}' RETURNING 1");
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        Assert.Equal(
            ["Ops\tnot_affected\twithdrawn\t1", "Reports\tnot_affected\twithdrawn\t1"],
            await host.RowsAsync("SELECT p.name, f.status, f.status_reason, f.level FROM assess.finding f JOIN assets.project p ON p.id = f.project_id ORDER BY p.name"));
    }
}
