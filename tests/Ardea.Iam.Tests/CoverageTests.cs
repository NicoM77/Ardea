using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Iam.Tests;

/// <summary>Coverage hints: what Ardea can check in a profile and what not (task 2.8; master plan 10.2, 11.6, 16.3).</summary>
public sealed class CoverageTests(PostgresServer server)
{
    [Fact]
    public async Task Every_kind_of_gap_is_named_with_its_count_and_the_rest_counts_as_fully_checkable()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var client = host.Client();
        await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "owner@example.org", displayName = "Owner", password = ApiTestHost.Password });
        var orgId = (await Json(await client.PostAsync("/api/v1/orgs", new { name = "Acme" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var projectId = (await Json(await client.PostAsync($"/api/v1/orgs/{orgId}/projects", new { name = "Shop" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await client.SendAsync(HttpMethod.Put, $"/api/v1/orgs/{orgId}/projects/{projectId}/components", new
        {
            components = new object[]
            {
                new { productKey = "wordpress" },                                       // no version, a platform without plugins
                new { productKey = "php", version = "8.3.12" },                         // fine
                new { productKey = "nginx", version = "1.24.0", packaging = "distribution" },
                new { customName = "Billing engine", kind = "other", version = "3.1" }, // not in the catalogue
            },
        });
        var connectionId = (await Json(await client.PostAsync($"/api/v1/orgs/{orgId}/repository-connections", new { name = "Public" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var linkId = (await Json(await client.PostAsync($"/api/v1/orgs/{orgId}/projects/{projectId}/repositories", new { connectionId, owner = "acme", name = "shop" }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        await host.ScalarAsync<int>($"""
            WITH c AS (
                INSERT INTO assets.component (organization_id, project_id, product_key, custom_name, kind, version, version_precision, packaging, protection, origin, repository_link_id, purl, created_at, changed_at)
                VALUES ('{orgId}', '{projectId}', NULL, 'lodash', 'library', '4.17.21', 'exact', 'upstream', 'none', 'repository', '{linkId}', 'pkg:npm/lodash@4.17.21', now(), now()),
                       ('{orgId}', '{projectId}', NULL, 'express', 'library', NULL, 'none', 'upstream', 'none', 'repository', '{linkId}', 'pkg:npm/express', now(), now()),
                       ('{orgId}', '{projectId}', NULL, 'serde', 'library', '1.0.210', 'exact', 'upstream', 'none', 'repository', '{linkId}', 'pkg:cargo/serde@1.0.210', now(), now()),
                       ('{orgId}', '{projectId}', 'dotnet', NULL, 'runtime', '10.0', 'line', 'unknown', 'none', 'repository', '{linkId}', NULL, now(), now())
                RETURNING 1
            ), s AS (
                INSERT INTO assets.component_suggestion (organization_id, project_id, repository_link_id, product_key, version, evidence, fingerprint, state, created_at)
                VALUES ('{orgId}', '{projectId}', '{linkId}', 'nodejs', '24', '[]', 'f', 'open', now()) RETURNING 1
            ), l AS (
                UPDATE assets.repository_link SET state = 'partial', state_reason = 'The dependency graph is not enabled.' WHERE id = '{linkId}' RETURNING 1
            )
            SELECT count(*)::int FROM c
            """);

        var coverage = await Json(await client.GetAsync($"/api/v1/orgs/{orgId}/profile/coverage"));

        var hints = coverage.GetProperty("hints").EnumerateArray().ToDictionary(h => h.GetProperty("kind").GetString()!, h => h);
        Assert.Equal(
            ["distribution_packages", "ecosystem_not_checkable", "missing_version", "not_in_catalog", "open_suggestions", "packages_without_lockfile", "platform_without_plugins", "repositories_partial", "version_line_only"],
            hints.Keys.Order(StringComparer.Ordinal));
        Assert.All(hints.Values, h => Assert.Equal(1, h.GetProperty("count").GetInt32()));
        Assert.Equal("cargo", hints["ecosystem_not_checkable"].GetProperty("examples")[0].GetProperty("detail").GetString());
        Assert.Equal("wordpress", hints["platform_without_plugins"].GetProperty("examples")[0].GetProperty("name").GetString());
        // 8 components; wordpress, nginx, billing, express, serde and dotnet 10.0 have a gap: php and lodash are fully checkable.
        Assert.Equal(8, coverage.GetProperty("components").GetInt32());
        Assert.Equal(2, coverage.GetProperty("fullyCheckable").GetInt32());

        // A plugin under the platform closes that gap.
        var wordpress = await host.ScalarAsync<Guid>($"SELECT id FROM assets.component WHERE product_key = 'wordpress'");
        await client.PostAsync($"/api/v1/orgs/{orgId}/projects/{projectId}/components", new { productKey = "woocommerce", version = "9.3.1", parentId = wordpress });
        var after = await Json(await client.GetAsync($"/api/v1/orgs/{orgId}/profile/coverage?projectId={projectId}"));
        Assert.DoesNotContain(after.GetProperty("hints").EnumerateArray(), h => h.GetProperty("kind").GetString() == "platform_without_plugins");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/orgs/{orgId}/profile/coverage?projectId={Guid.NewGuid()}")).StatusCode);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        if (response.StatusCode != status)
        {
            Assert.Fail($"Expected {(int)status}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
