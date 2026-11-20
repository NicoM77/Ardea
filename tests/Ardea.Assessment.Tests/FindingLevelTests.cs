using System.Text.Json;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assessment.Tests;

public sealed class FindingLevelTests(PostgresServer server)
{
    [Fact]
    public async Task A_finding_stores_level_and_factors_and_development_components_stay_low()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var platform = await host.AddProjectAsync(acme, "Platform Ops", criticality: "high", exposure: "internet");
        await host.AddComponentAsync(acme, platform, c => (c.CustomName, c.Kind, c.Version, c.Purl, c.Origin, c.DependencyScope) =
            ("lodash", "library", "4.17.20", "pkg:npm/lodash@4.17.20", "repository", "runtime"));
        var tooling = await host.AddProjectAsync(acme, "Tooling", criticality: "high", exposure: "internet");
        await host.AddComponentAsync(acme, tooling, c => (c.CustomName, c.Kind, c.Version, c.Purl, c.Origin, c.DependencyScope) =
            ("lodash", "library", "4.17.20", "pkg:npm/lodash@4.17.20", "repository", "development"));

        var entry = await host.AddEntryAsync(new TestEntry("CVE-2026-6001", new TestAffected { PurlType = "npm", PurlName = "lodash", Fixed = "4.17.21" })
        {
            Score = 9.8,
            Exploitation = "poc",
        });
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();

        Assert.Equal(
            ["Platform Ops\t5", "Tooling\t2"],
            await host.RowsAsync("SELECT p.name, f.level FROM assess.finding f JOIN assets.project p ON p.id = f.project_id ORDER BY p.name"));
        Assert.Equal(5, await host.ScalarAsync<int>("SELECT level FROM assess.alert"));

        using var factors = JsonDocument.Parse(await host.ScalarAsync<string>(
            "SELECT f.factors::text FROM assess.finding f JOIN assets.project p ON p.id = f.project_id WHERE p.name = 'Platform Ops'"));
        var root = factors.RootElement;
        Assert.Equal(1, root.GetProperty("rulesetVersion").GetInt32());
        Assert.Equal(9, root.GetProperty("points").GetInt32());
        Assert.Equal(
            ["severity critical 4", "exploitation poc 2", "affectedness confirmed 1", "criticality high 1", "exposure internet 1", "protection none 0", "authorityWarning no 0", "epss low 0"],
            root.GetProperty("factors").EnumerateArray().Select(f => $"{f.GetProperty("factor").GetString()} {f.GetProperty("value").GetString()} {f.GetProperty("points").GetInt32()}"));
        Assert.Equal("version 4.17.20 in an affected range", root.GetProperty("factors")[2].GetProperty("evidence").GetString());

        var development = await host.ScalarAsync<string>(
            "SELECT f.factors->'caps'->0->>'bound' FROM assess.finding f JOIN assets.project p ON p.id = f.project_id WHERE p.name = 'Tooling'");
        Assert.Equal("development", development);
    }

    [Fact]
    public async Task A_new_signal_raises_the_level_and_the_same_input_changes_nothing()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var project = await host.AddProjectAsync(acme, "Platform Ops", criticality: "high");
        await host.AddComponentAsync(acme, project, c => (c.ProductKey, c.Kind, c.Version) = ("postgresql", "database", "16.2"));
        var entry = await host.AddEntryAsync(new TestEntry("CVE-2026-6002", new TestAffected { ProductKey = "postgresql", Fixed = "16.4" }) { Score = 9.4 });
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        Assert.Equal(4, await host.ScalarAsync<int>("SELECT level FROM assess.finding"));
        var factors = await host.ScalarAsync<string>("SELECT factors::text FROM assess.finding");

        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        Assert.Equal(factors, await host.ScalarAsync<string>("SELECT factors::text FROM assess.finding"));

        await host.ScalarAsync<int>($"UPDATE intel.security_entry SET exploitation = 'active', revision = 2 WHERE id = '{entry}' RETURNING 1");
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        Assert.Equal("5\t9\t2", (await host.RowsAsync("SELECT f.level, (f.factors->>'points'), a.assessed_entry_revision FROM assess.finding f JOIN assess.alert a ON a.id = f.alert_id")).Single());
    }
}

public sealed class RecomputeCommandTests(PostgresServer server)
{
    [Fact]
    public async Task The_command_assesses_every_alert_again_and_restores_levels_from_the_stored_input()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var project = await host.AddProjectAsync(acme, "Platform Ops", criticality: "high");
        await host.AddComponentAsync(acme, project, c => (c.ProductKey, c.Kind, c.Version) = ("postgresql", "database", "16.2"));
        var entry = await host.AddEntryAsync(new TestEntry("CVE-2026-6003", new TestAffected { ProductKey = "postgresql", Fixed = "16.4" }) { Score = 9.4 });
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        var factors = await host.ScalarAsync<string>("SELECT factors::text FROM assess.finding");

        await host.ScalarAsync<int>("UPDATE assess.finding SET level = 2, factors = '{\"rulesetVersion\": 0}' RETURNING 1");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var command = new AssessmentModule().Commands.Single(c => c.Name == "assess-recompute");
            Assert.Equal(0, await command.RunAsync(scope.ServiceProvider, [], CancellationToken.None));
        }

        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'assess.assess-alert' AND state = 'pending'"));
        await host.RunJobsAsync();
        Assert.Equal(4, await host.ScalarAsync<int>("SELECT level FROM assess.finding"));
        Assert.Equal(factors, await host.ScalarAsync<string>("SELECT factors::text FROM assess.finding"));
    }
}
