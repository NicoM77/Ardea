using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Iam.Tests;

public sealed class RepositoryLinkTests(PostgresServer server)
{
    [Fact]
    public async Task A_member_links_a_repository_which_is_due_at_once_and_removing_it_removes_its_components()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var (client, orgId, projectId, connectionId) = await SetUpAsync(host);
        using var _ = client;
        var path = $"/api/v1/orgs/{orgId}/projects/{projectId}/repositories";

        var link = await Json(await client.PostAsync(path, new { connectionId, owner = "acme", name = "portal", path = "/services/api/" }), HttpStatusCode.Created);
        Assert.Equal("pending", link.GetProperty("state").GetString());
        Assert.Equal("services/api", link.GetProperty("path").GetString());
        var linkId = link.GetProperty("id").GetGuid();
        Assert.True(await host.ScalarAsync<bool>($"SELECT next_check_at <= now() FROM assets.repository_link WHERE id = '{linkId}'"));

        await ExpectAsync(await client.PostAsync(path, new { connectionId, owner = "acme", name = "portal", path = "services/api" }), HttpStatusCode.Conflict, "link_exists");
        await ExpectAsync(await client.PostAsync(path, new { connectionId, owner = "acme", name = "portal", path = "../etc" }), HttpStatusCode.BadRequest, "validation_failed");
        await ExpectAsync(await client.PostAsync(path, new { connectionId = Guid.NewGuid(), owner = "acme", name = "portal" }), HttpStatusCode.BadRequest, "validation_failed");

        await host.ScalarAsync<int>($"""
            INSERT INTO assets.component (organization_id, project_id, product_key, kind, version, version_precision, packaging, protection, origin, repository_link_id, purl, created_at, changed_at)
            VALUES ('{orgId}', '{projectId}', 'newtonsoft-json', 'library', '13.0.3', 'exact', 'upstream', 'none', 'repository', '{linkId}', 'pkg:nuget/Newtonsoft.Json@13.0.3', now(), now())
            RETURNING 1
            """);
        Assert.Equal(1, (await Json(await client.GetAsync(path))).EnumerateArray().Single().GetProperty("componentCount").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(HttpMethod.Delete, $"{path}/{linkId}")).StatusCode);
        Assert.Equal(0L, await host.ScalarAsync<long>($"SELECT count(*) FROM assets.component WHERE project_id = '{projectId}'"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'assets.profile-changed'"));
    }

    [Fact]
    public async Task A_confirmed_suggestion_becomes_a_repository_component_with_its_evidence_and_a_dismissed_one_stays_dismissed()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var (client, orgId, projectId, connectionId) = await SetUpAsync(host);
        using var _ = client;
        var linkId = (await Json(await client.PostAsync($"/api/v1/orgs/{orgId}/projects/{projectId}/repositories", new { connectionId, owner = "acme", name = "portal" }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var aspnet = await SuggestAsync(host, orgId, projectId, linkId, "aspnetcore", "10.0", "FROM mcr.microsoft.com/dotnet/aspnet:10.0");
        var node = await SuggestAsync(host, orgId, projectId, linkId, "nodejs", "24", "FROM node:24-bookworm-slim");
        var path = $"/api/v1/orgs/{orgId}/projects/{projectId}/suggestions";

        var open = await Json(await client.GetAsync(path));
        Assert.Equal(["aspnetcore", "nodejs"], open.EnumerateArray().Select(s => s.GetProperty("productKey").GetString()));
        Assert.Equal("Dockerfile", open.EnumerateArray().First().GetProperty("evidence").EnumerateArray().Single().GetProperty("file").GetString());

        var accepted = await Json(await client.SendAsync(HttpMethod.Patch, $"{path}/{aspnet}", new { decision = "accept", version = "10.0.1" }));
        Assert.Equal("accepted", accepted.GetProperty("state").GetString());
        var componentId = accepted.GetProperty("componentId").GetGuid();
        Assert.Equal("repository|10.0.1|Dockerfile", await host.ScalarAsync<string>(
            $"SELECT origin || '|' || version || '|' || (evidence -> 0 ->> 'file') FROM assets.component WHERE id = '{componentId}'"));
        Assert.True(await host.ScalarAsync<long>($"SELECT count(*) FROM assets.component_key WHERE component_id = '{componentId}'") > 0);

        Assert.Equal("dismissed", (await Json(await client.SendAsync(HttpMethod.Patch, $"{path}/{node}", new { decision = "dismiss" }))).GetProperty("state").GetString());
        await ExpectAsync(await client.SendAsync(HttpMethod.Patch, $"{path}/{node}", new { decision = "accept" }), HttpStatusCode.Conflict, "suggestion_decided");
        Assert.Empty((await Json(await client.GetAsync(path))).EnumerateArray());
        Assert.Single((await Json(await client.GetAsync($"{path}?state=dismissed"))).EnumerateArray());
    }

    [Fact]
    public async Task The_schedule_claims_due_links_across_organisations_and_only_once()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var (client, orgId, projectId, connectionId) = await SetUpAsync(host);
        using var _ = client;
        await client.PostAsync($"/api/v1/orgs/{orgId}/projects/{projectId}/repositories", new { connectionId, owner = "acme", name = "portal" });
        await client.PostAsync($"/api/v1/orgs/{orgId}/projects/{projectId}/repositories", new { connectionId, owner = "acme", name = "website" });

        await using var dataSource = Npgsql.NpgsqlDataSource.Create(host.Database.AppConnectionString("tests"));
        await using var claim = dataSource.CreateCommand("SELECT count(*) FROM assets.claim_due_repository_links(10, interval '30 minutes')");
        Assert.Equal(2L, (long)(await claim.ExecuteScalarAsync())!);
        Assert.Equal(0L, (long)(await claim.ExecuteScalarAsync())!);
        await using var direct = dataSource.CreateCommand("SELECT count(*) FROM assets.repository_link");
        Assert.Equal(0L, (long)(await direct.ExecuteScalarAsync())!);
    }

    private static async Task<Guid> SuggestAsync(ApiTestHost host, Guid orgId, Guid projectId, Guid linkId, string product, string version, string value) =>
        await host.ScalarAsync<Guid>($$"""
            INSERT INTO assets.component_suggestion (organization_id, project_id, repository_link_id, product_key, version, evidence, fingerprint, state, created_at)
            VALUES ('{{orgId}}', '{{projectId}}', '{{linkId}}', '{{product}}', '{{version}}', '[{"file":"Dockerfile","line":1,"value":"{{value}}"}]', md5('{{product}}{{version}}'), 'open', now())
            RETURNING id
            """);

    private static async Task<(ApiClient, Guid, Guid, Guid)> SetUpAsync(ApiTestHost host)
    {
        var client = host.Client();
        await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "owner@example.org", displayName = "Owner", password = ApiTestHost.Password });
        var orgId = (await Json(await client.PostAsync("/api/v1/orgs", new { name = "Acme" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var projectId = (await Json(await client.PostAsync($"/api/v1/orgs/{orgId}/projects", new { name = "Portal" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var connectionId = (await Json(await client.PostAsync($"/api/v1/orgs/{orgId}/repository-connections", new { name = "Public" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        return (client, orgId, projectId, connectionId);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        if (response.StatusCode != status)
        {
            Assert.Fail($"Expected {(int)status}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode status, string code) =>
        Assert.Equal(code, (await Json(response, status)).GetProperty("code").GetString());
}
