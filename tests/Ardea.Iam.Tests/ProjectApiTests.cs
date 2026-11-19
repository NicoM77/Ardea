using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Iam.Tests;

/// <summary>Projects and components, with profile replacement as a reconcile (task 2.3; master plan 4.5, 4.9, 10.2).</summary>
public sealed class ProjectApiTests(PostgresServer server)
{
    [Fact]
    public async Task A_project_holds_catalogue_and_custom_components_and_every_change_is_audited_and_queues_the_reverse_search()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var (owner, orgId) = await OrgAsync(host);
        using var _ = owner;

        var project = await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/projects", new { name = "Customer portal", exposure = "internet", criticality = "high" }), HttpStatusCode.Created);
        var projectId = project.GetProperty("id").GetGuid();

        var postgres = await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/projects/{projectId}/components", new { productKey = "postgresql", version = "17.2" }), HttpStatusCode.Created);
        Assert.Equal("database", postgres.GetProperty("kind").GetString());
        Assert.True(postgres.GetProperty("inCatalog").GetBoolean());
        Assert.False(string.IsNullOrEmpty(postgres.GetProperty("versionScheme").GetString()));

        var custom = await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/projects/{projectId}/components",
            new { customVendor = "Acme", customName = "Billing engine", kind = "service" }), HttpStatusCode.Created);
        Assert.False(custom.GetProperty("inCatalog").GetBoolean());

        await ExpectProblem(await owner.PostAsync($"/api/v1/orgs/{orgId}/projects/{projectId}/components", new { productKey = "no-such-product" }), HttpStatusCode.BadRequest, "validation_failed");
        await ExpectProblem(await owner.PostAsync($"/api/v1/orgs/{orgId}/projects/{projectId}/components", new { customName = "Mail", kind = "service", version = "1" }), HttpStatusCode.BadRequest, "validation_failed");

        // Changing the version keeps the id.
        var postgresId = postgres.GetProperty("id").GetGuid();
        var changed = await Json(await owner.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}/projects/{projectId}/components/{postgresId}", new { version = "17.3", protection = "effective", protectionNote = "Only reachable from the app server" }));
        Assert.Equal(postgresId, changed.GetProperty("id").GetGuid());
        Assert.Equal("17.3", changed.GetProperty("version").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/orgs/{orgId}/projects/{projectId}/components/{custom.GetProperty("id").GetGuid()}")).StatusCode);

        var detail = await Json(await owner.GetAsync($"/api/v1/orgs/{orgId}/projects/{projectId}"));
        Assert.Equal(1, detail.GetProperty("project").GetProperty("componentCount").GetInt32());

        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'assets.profile-changed' AND state = 'pending'"));
        Assert.Equal(4L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.audit_event WHERE action IN ('component.added', 'component.changed', 'component.removed')"));
    }

    [Fact]
    public async Task Replacing_the_profile_with_one_changed_version_keeps_every_component_id()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var (owner, orgId) = await OrgAsync(host);
        using var _ = owner;
        var projectId = (await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/projects", new { name = "Shop" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var path = $"/api/v1/orgs/{orgId}/projects/{projectId}/components";

        var first = await Json(await owner.SendAsync(HttpMethod.Put, path, new
        {
            components = new object[]
            {
                new { @ref = "wp", productKey = "wordpress", version = "6.6.2" },
                new { productKey = "woocommerce", version = "9.3.1", parentRef = "wp" },
                new { productKey = "php", version = "8.3.12" },
                new { productKey = "nginx", version = "1.26.2" },
                new { customName = "Theme by agency", kind = "plugin", parentRef = "wp" },
            },
        }));
        Assert.Equal(5, first.GetProperty("created").GetInt32());
        var before = Ids(first);
        var wordpress = before["wordpress"];
        Assert.Equal(wordpress, Components(first).Single(c => c.GetProperty("productKey").GetString() == "woocommerce").GetProperty("parentId").GetGuid());

        // The wizard sends the whole list again, without ids, with one version raised.
        var second = await Json(await owner.SendAsync(HttpMethod.Put, path, new
        {
            components = new object[]
            {
                new { @ref = "wp", productKey = "wordpress", version = "6.7.1" },
                new { productKey = "woocommerce", version = "9.3.1", parentRef = "wp" },
                new { productKey = "php", version = "8.3.12" },
                new { productKey = "nginx", version = "1.26.2" },
                new { customName = "Theme by agency", kind = "plugin", parentRef = "wp" },
            },
        }));
        Assert.Equal((0, 1, 0, 4), Counts(second));
        Assert.Equal(before, Ids(second));

        // Nothing changed: nothing written. A missing item is removed, the rest keep their ids.
        Assert.Equal((0, 0, 0, 5), Counts(await Json(await owner.SendAsync(HttpMethod.Put, path, new
        {
            components = new object[]
            {
                new { id = wordpress, productKey = "wordpress", version = "6.7.1" },
                new { productKey = "woocommerce", version = "9.3.1", parentId = wordpress },
                new { productKey = "php", version = "8.3.12" },
                new { productKey = "nginx", version = "1.26.2" },
                new { customName = "Theme by agency", kind = "plugin", parentId = wordpress },
            },
        }))));
        var third = await Json(await owner.SendAsync(HttpMethod.Put, path, new
        {
            components = new object[]
            {
                new { id = wordpress, productKey = "wordpress", version = "6.7.1" },
                new { productKey = "woocommerce", version = "9.3.1", parentId = wordpress },
                new { productKey = "php", version = "8.3.12" },
            },
        }));
        Assert.Equal((0, 0, 2, 3), Counts(third));
        Assert.Equal(before["php"], Ids(third)["php"]);

        await ExpectProblem(await owner.SendAsync(HttpMethod.Put, path, new { components = new object[] { new { @ref = "a", productKey = "php", parentRef = "a" } } }),
            HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(3L, await host.ScalarAsync<long>($"SELECT count(*) FROM assets.component WHERE project_id = '{projectId}'"));
        Assert.Equal(3L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.audit_event WHERE action = 'profile.replaced'"));
    }

    [Fact]
    public async Task Components_from_a_repository_change_only_in_their_context_and_survive_a_replacement()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var (owner, orgId) = await OrgAsync(host);
        using var _ = owner;
        var projectId = (await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/projects", new { name = "API" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var repositoryComponent = await host.ScalarAsync<Guid>(
            $"""
            INSERT INTO assets.component (organization_id, project_id, product_key, kind, version, packaging, protection, origin, purl, created_at, changed_at)
            VALUES ('{orgId}', '{projectId}', 'newtonsoft-json', 'library', '13.0.1', 'upstream', 'none', 'repository', 'pkg:nuget/Newtonsoft.Json@13.0.1', now(), now())
            RETURNING id
            """);
        var path = $"/api/v1/orgs/{orgId}/projects/{projectId}/components";

        await ExpectProblem(await owner.SendAsync(HttpMethod.Patch, $"{path}/{repositoryComponent}", new { version = "13.0.3" }), HttpStatusCode.Conflict, "repository_component");
        await ExpectProblem(await owner.SendAsync(HttpMethod.Delete, $"{path}/{repositoryComponent}"), HttpStatusCode.Conflict, "repository_component");
        Assert.Equal("internet", (await Json(await owner.SendAsync(HttpMethod.Patch, $"{path}/{repositoryComponent}", new { exposure = "internet" }))).GetProperty("exposure").GetString());

        // Replacing the manual list leaves it alone.
        await Json(await owner.SendAsync(HttpMethod.Put, path, new { components = new object[] { new { productKey = "dotnet", version = "10.0.0" } } }));
        Assert.Equal(1L, await host.ScalarAsync<long>($"SELECT count(*) FROM assets.component WHERE id = '{repositoryComponent}'"));

        // Detached, it is the user's to change.
        Assert.Equal("manual", (await Json(await owner.PostAsync($"{path}/{repositoryComponent}/detach"))).GetProperty("origin").GetString());
        Assert.Equal("13.0.3", (await Json(await owner.SendAsync(HttpMethod.Patch, $"{path}/{repositoryComponent}", new { version = "13.0.3" }))).GetProperty("version").GetString());
    }

    [Fact]
    public async Task Viewers_read_members_maintain_and_archived_projects_leave_the_list()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var (owner, orgId) = await OrgAsync(host);
        using var _ = owner;
        var link = (await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/invitations", new { email = "viewer@example.org", role = "viewer" }), HttpStatusCode.Created)).GetProperty("link").GetString()!;
        using var viewer = host.Client();
        await viewer.PostAsync("/api/v1/auth/register", new { email = "viewer@example.org", displayName = "Viewer", password = ApiTestHost.Password });
        Assert.Equal(HttpStatusCode.OK, (await viewer.PostAsync($"/api/v1/invitations/{link[(link.LastIndexOf('/') + 1)..]}/accept")).StatusCode);

        var projectId = (await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/projects", new { name = "Intranet" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        Assert.Single((await Json(await viewer.GetAsync($"/api/v1/orgs/{orgId}/projects"))).EnumerateArray());
        await ExpectProblem(await viewer.PostAsync($"/api/v1/orgs/{orgId}/projects", new { name = "Mine" }), HttpStatusCode.Forbidden, "insufficient_role");
        await ExpectProblem(await viewer.PostAsync($"/api/v1/orgs/{orgId}/projects/{projectId}/components", new { productKey = "php" }), HttpStatusCode.Forbidden, "insufficient_role");

        Assert.Equal(HttpStatusCode.NoContent, (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/orgs/{orgId}/projects/{projectId}")).StatusCode);
        Assert.Empty((await Json(await owner.GetAsync($"/api/v1/orgs/{orgId}/projects"))).EnumerateArray());
        Assert.Single((await Json(await owner.GetAsync($"/api/v1/orgs/{orgId}/projects?archived=true"))).EnumerateArray());
        Assert.False((await Json(await owner.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}/projects/{projectId}", new { archived = false }))).GetProperty("archived").GetBoolean());
    }

    [Fact]
    public async Task The_catalogue_search_helps_with_input()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var (owner, _) = await OrgAsync(host);
        using var client = owner;

        var found = await Json(await client.GetAsync("/api/v1/catalog/products?q=postgres"));
        Assert.Equal("postgresql", found.EnumerateArray().First().GetProperty("key").GetString());
        Assert.Empty((await Json(await client.GetAsync("/api/v1/catalog/products?q="))).EnumerateArray());
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client().GetAsync("/api/v1/catalog/products?q=postgres")).StatusCode);
    }

    private static async Task<(ApiClient Client, Guid OrgId)> OrgAsync(ApiTestHost host)
    {
        var client = host.Client();
        await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "owner@example.org", displayName = "Owner", password = ApiTestHost.Password });
        var org = await Json(await client.PostAsync("/api/v1/orgs", new { name = "Acme" }), HttpStatusCode.Created);
        return (client, org.GetProperty("id").GetGuid());
    }

    private static IEnumerable<JsonElement> Components(JsonElement response) => response.GetProperty("components").EnumerateArray();

    private static Dictionary<string, Guid> Ids(JsonElement response) =>
        Components(response).ToDictionary(c => c.GetProperty("productKey").GetString() ?? c.GetProperty("name").GetString()!, c => c.GetProperty("id").GetGuid());

    private static (int Created, int Updated, int Removed, int Unchanged) Counts(JsonElement r) =>
        (r.GetProperty("created").GetInt32(), r.GetProperty("updated").GetInt32(), r.GetProperty("removed").GetInt32(), r.GetProperty("unchanged").GetInt32());

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        if (response.StatusCode != status)
        {
            Assert.Fail($"Expected {(int)status}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task ExpectProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var problem = await Json(response, status);
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }
}
