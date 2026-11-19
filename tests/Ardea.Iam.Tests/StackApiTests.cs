using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Iam.Tests;

/// <summary>
/// The organisation's stack (task 5a.2; master plan 17.2, ADR 046): a system project created with the organisation, read
/// through <c>GET stack</c>, kept out of the project list, and changed only through its entries, which carry their version
/// precision and notice threshold.
/// </summary>
public sealed class StackApiTests(PostgresServer server)
{
    [Fact]
    public async Task A_new_organisation_has_one_stack_which_is_not_a_project_and_cannot_be_renamed_archived_or_linked()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var (owner, orgId) = await OrgAsync(host);
        using var _ = owner;
        var path = $"/api/v1/orgs/{orgId}";

        var stack = await Json(await owner.GetAsync($"{path}/stack"));
        var stackId = stack.GetProperty("projectId").GetGuid();
        Assert.Empty(stack.GetProperty("components").EnumerateArray());
        Assert.Equal(1L, await host.ScalarAsync<long>($"SELECT count(*) FROM assets.project WHERE organization_id = '{orgId}' AND kind = 'organization_stack'"));

        // Not among the projects, and not a gap of the profile.
        Assert.Empty((await Json(await owner.GetAsync($"{path}/projects?archived=true"))).EnumerateArray());
        await ExpectProblem(await owner.SendAsync(HttpMethod.Patch, $"{path}/projects/{stackId}", new { name = "Mine" }), HttpStatusCode.Conflict, "stack_project");
        await ExpectProblem(await owner.SendAsync(HttpMethod.Delete, $"{path}/projects/{stackId}"), HttpStatusCode.Conflict, "stack_project");
        var connectionId = (await Json(await owner.PostAsync($"{path}/repository-connections", new { name = "Public" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await ExpectProblem(await owner.PostAsync($"{path}/projects/{stackId}/repositories", new { connectionId, owner = "acme", name = "shop" }), HttpStatusCode.Conflict, "stack_project");

        // Its entries are components like any other: same endpoints, same search keys, same reverse search.
        var postgres = await Json(await owner.PostAsync($"{path}/projects/{stackId}/components", new { productKey = "postgresql" }), HttpStatusCode.Created);
        Assert.Equal("none", postgres.GetProperty("versionPrecision").GetString());
        Assert.Equal("only_important", postgres.GetProperty("noticeThreshold").GetString());
        Assert.True(await host.ScalarAsync<long>($"SELECT count(*) FROM assets.component_key WHERE component_id = '{postgres.GetProperty("id").GetGuid()}'") > 0);
        Assert.Single((await Json(await owner.GetAsync($"{path}/stack"))).GetProperty("components").EnumerateArray());
        Assert.Equal(0, (await Json(await owner.GetAsync($"{path}/profile/coverage"))).GetProperty("components").GetInt32());
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'assets.profile-changed' AND state = 'pending'"));
    }

    [Fact]
    public async Task An_entry_states_its_precision_and_threshold_or_gets_them_from_its_version()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var (owner, orgId) = await OrgAsync(host);
        using var _ = owner;
        var stackId = (await Json(await owner.GetAsync($"/api/v1/orgs/{orgId}/stack"))).GetProperty("projectId").GetGuid();
        var components = $"/api/v1/orgs/{orgId}/projects/{stackId}/components";

        var line = await Json(await owner.PostAsync(components, new { productKey = "postgresql", version = "17" }), HttpStatusCode.Created);
        Assert.Equal("line", line.GetProperty("versionPrecision").GetString());
        var php = await Json(await owner.PostAsync(components, new { productKey = "php", version = "8.3", versionPrecision = "line", noticeThreshold = "everything" }), HttpStatusCode.Created);
        Assert.Equal("line", php.GetProperty("versionPrecision").GetString());
        Assert.Equal("everything", php.GetProperty("noticeThreshold").GetString());

        // A precision must fit the version, a threshold must be one of the two.
        await ExpectProblem(await owner.PostAsync(components, new { productKey = "nginx", version = "1.27", versionPrecision = "none" }), HttpStatusCode.BadRequest, "validation_failed");
        await ExpectProblem(await owner.PostAsync(components, new { productKey = "nginx", versionPrecision = "exact" }), HttpStatusCode.BadRequest, "validation_failed");
        await ExpectProblem(await owner.PostAsync(components, new { productKey = "nginx", noticeThreshold = "some" }), HttpStatusCode.BadRequest, "validation_failed");

        // A new version without a stated precision gets the one that follows from it; the threshold stays.
        var exact = await Json(await owner.SendAsync(HttpMethod.Patch, $"{components}/{php.GetProperty("id").GetGuid()}", new { version = "8.3.27" }));
        Assert.Equal("exact", exact.GetProperty("versionPrecision").GetString());
        Assert.Equal("everything", exact.GetProperty("noticeThreshold").GetString());
        var cleared = await Json(await owner.SendAsync(HttpMethod.Patch, $"{components}/{line.GetProperty("id").GetGuid()}", new { version = "" }));
        Assert.Equal("none", cleared.GetProperty("versionPrecision").GetString());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("version").ValueKind);
    }

    private static async Task<(ApiClient Client, Guid OrgId)> OrgAsync(ApiTestHost host)
    {
        var client = host.Client();
        await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "owner@example.org", displayName = "Owner", password = ApiTestHost.Password });
        var org = await Json(await client.PostAsync("/api/v1/orgs", new { name = "Acme" }), HttpStatusCode.Created);
        return (client, org.GetProperty("id").GetGuid());
    }

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
