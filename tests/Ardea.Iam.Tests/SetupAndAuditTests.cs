using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Iam.Tests;

/// <summary>The setup wizard's state and the organisation's audit log (task 2.7; master plan 9.4, 10.2, 11.6).</summary>
public sealed class SetupAndAuditTests(PostgresServer server)
{
    [Fact]
    public async Task The_wizard_saves_its_steps_in_order_and_completes_once()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var owner = await SetUpAsync(host);
        var orgId = await CreateOrgAsync(owner, "Acme");

        var empty = await Json(await owner.GetAsync($"/api/v1/orgs/{orgId}/setup"));
        Assert.Empty(empty.GetProperty("stepsDone").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("completedAt").ValueKind);

        var projectId = Guid.CreateVersion7();
        await Json(await owner.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}/setup", new { stepDone = "operations" }));
        var saved = await Json(await owner.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}/setup", new { stepDone = "project", firstProjectId = projectId }));
        Assert.Equal(["project", "operations"], saved.GetProperty("stepsDone").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(projectId, saved.GetProperty("firstProjectId").GetGuid());

        // The stack comes before the projects (master plan 11.6 step 1b, task 5a.4), also when an organisation that
        // started the wizard before saves it afterwards.
        var stack = await Json(await owner.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}/setup", new { stepDone = "stack" }));
        Assert.Equal(["stack", "project", "operations"], stack.GetProperty("stepsDone").EnumerateArray().Select(s => s.GetString()));

        // Saving a step twice changes nothing.
        await Json(await owner.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}/setup", new { stepDone = "project" }));
        Assert.Equal(3, (await Json(await owner.GetAsync($"/api/v1/orgs/{orgId}/setup"))).GetProperty("stepsDone").GetArrayLength());
        await ExpectProblem(await owner.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}/setup", new { stepDone = "dance" }), HttpStatusCode.BadRequest, "validation_failed");

        var completed = await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/setup/complete"));
        var at = completed.GetProperty("completedAt").GetDateTimeOffset();
        Assert.Equal(at, (await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/setup/complete"))).GetProperty("completedAt").GetDateTimeOffset());

        // Viewers read the state but do not change it.
        using var viewer = await JoinAsync(host, owner, orgId, "viewer@example.org", "viewer");
        Assert.Equal(at, (await Json(await viewer.GetAsync($"/api/v1/orgs/{orgId}/setup"))).GetProperty("completedAt").GetDateTimeOffset());
        await ExpectProblem(await viewer.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}/setup", new { stepDone = "coverage" }), HttpStatusCode.Forbidden, "insufficient_role");
    }

    [Fact]
    public async Task Administrators_page_through_their_organisations_audit_log_newest_first()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var owner = await SetUpAsync(host);
        var orgId = await CreateOrgAsync(owner, "Acme");
        var otherOrg = await CreateOrgAsync(owner, "Elsewhere");
        using var member = await JoinAsync(host, owner, orgId, "member@example.org", "member");
        await Json(await member.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}/setup", new { stepDone = "project" }));
        await Expect(HttpStatusCode.NoContent, await member.SendAsync(HttpMethod.Delete, $"/api/v1/orgs/{orgId}/members/{await UserIdAsync(host, "member@example.org")}"));

        // A member may not read it.
        using var viewer = await JoinAsync(host, owner, orgId, "viewer@example.org", "viewer");
        await ExpectProblem(await viewer.GetAsync($"/api/v1/orgs/{orgId}/audit"), HttpStatusCode.Forbidden, "insufficient_role");

        var actions = new List<string>();
        var ids = new HashSet<Guid>();
        var actors = new Dictionary<string, (string Kind, string? Name)>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await Json(await owner.GetAsync($"/api/v1/orgs/{orgId}/audit?limit=2" + (cursor is null ? string.Empty : $"&cursor={cursor}")));
            var items = page.GetProperty("items").EnumerateArray().ToList();
            Assert.InRange(items.Count, 1, 2);
            foreach (var item in items)
            {
                Assert.True(ids.Add(item.GetProperty("id").GetGuid()), "an event appears on two pages");
                var action = item.GetProperty("action").GetString()!;
                actions.Add(action);
                actors.TryAdd(action, (item.GetProperty("actorKind").GetString()!, item.GetProperty("actorName").GetString()));
            }

            cursor = page.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 20);

        // Newest first, every event once, only this organisation's.
        Assert.Equal("org.member_joined", actions[0]);
        Assert.Equal("org.created", actions[^1]);
        Assert.Equal(await host.ScalarAsync<long>($"SELECT count(*) FROM platform.audit_event WHERE organization_id = '{orgId}'"), actions.Count);
        Assert.Contains("org.setup_saved", actions);
        Assert.Contains("org.member_left", actions);
        Assert.Equal(1, actions.Count(a => a == "org.created"));

        // The owner by name; the member who left only as a former member.
        Assert.Equal(("member", "Owner"), actors["org.created"]);
        Assert.Equal(("former_member", null), actors["org.setup_saved"]);

        await ExpectProblem(await owner.GetAsync($"/api/v1/orgs/{orgId}/audit?cursor=nonsense"), HttpStatusCode.BadRequest, "validation_failed");
        var other = await Json(await owner.GetAsync($"/api/v1/orgs/{otherOrg}/audit"));
        Assert.Equal(["org.created"], other.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("action").GetString()));
    }

    private static async Task<ApiClient> SetUpAsync(ApiTestHost host)
    {
        var client = host.Client();
        await Expect(HttpStatusCode.NoContent, await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "owner@example.org", displayName = "Owner", password = ApiTestHost.Password }));
        return client;
    }

    private static async Task<ApiClient> RegisterAsync(ApiTestHost host, string email)
    {
        var client = host.Client();
        await Expect(HttpStatusCode.NoContent, await client.PostAsync("/api/v1/auth/register", new { email, displayName = email.Split('@')[0], password = ApiTestHost.Password }));
        return client;
    }

    private static async Task<Guid> CreateOrgAsync(ApiClient client, string name) =>
        (await Json(await client.PostAsync("/api/v1/orgs", new { name }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    private static async Task<ApiClient> JoinAsync(ApiTestHost host, ApiClient admin, Guid orgId, string email, string role)
    {
        var link = (await Json(await admin.PostAsync($"/api/v1/orgs/{orgId}/invitations", new { email, role }), HttpStatusCode.Created)).GetProperty("link").GetString()!;
        var client = await RegisterAsync(host, email);
        await Expect(HttpStatusCode.OK, await client.PostAsync($"/api/v1/invitations/{link[(link.LastIndexOf('/') + 1)..]}/accept"));
        return client;
    }

    private static Task<Guid> UserIdAsync(ApiTestHost host, string email) =>
        host.ScalarAsync<Guid>($"SELECT id FROM iam.\"user\" WHERE normalized_email = '{email.ToUpperInvariant()}'");

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        await Expect(status, response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task Expect(HttpStatusCode status, HttpResponseMessage response)
    {
        if (response.StatusCode != status)
        {
            Assert.Fail($"Expected {(int)status}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    private static async Task ExpectProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await Expect(status, response);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.TryGetProperty("code", out var c) ? c.GetString() : null);
    }
}
