using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Iam.Tests;

/// <summary>Organisations, memberships, roles, invitations and the MFA requirement (task 2.1; master plan 9.1, 9.3, 10.2).</summary>
public sealed class OrganizationTests(PostgresServer server)
{
    [Fact]
    public async Task A_new_organisation_has_its_founder_as_owner_and_appears_in_the_session()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var owner = await SetUpAsync(host);

        var created = await Json(await owner.PostAsync("/api/v1/orgs", new { name = "Acme Zürich AG" }), HttpStatusCode.Created);
        Assert.Equal("acme-zurich-ag", created.GetProperty("slug").GetString());
        Assert.Equal("owner", created.GetProperty("role").GetString());

        var session = await Json(await owner.SessionAsync());
        var org = Assert.Single(session.GetProperty("organizations").EnumerateArray());
        Assert.Equal("acme-zurich-ag", org.GetProperty("slug").GetString());
        Assert.Equal("owner", org.GetProperty("role").GetString());

        await ExpectProblem(await owner.PostAsync("/api/v1/orgs", new { name = "Other", slug = "acme-zurich-ag" }), HttpStatusCode.Conflict, "slug_taken");
        await ExpectProblem(await owner.PostAsync("/api/v1/orgs", new { name = "Bad", slug = "Not A Slug" }), HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.audit_event WHERE action = 'org.created' AND organization_id IS NOT NULL"));
    }

    [Fact]
    public async Task An_organisation_does_not_exist_for_someone_outside_it()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var owner = await SetUpAsync(host);
        var orgId = await CreateOrgAsync(owner, "Acme");
        using var stranger = await RegisterAsync(host, "stranger@example.org");
        var strangersOwn = await CreateOrgAsync(stranger, "Stranger Ltd");

        await ExpectProblem(await stranger.GetAsync($"/api/v1/orgs/{orgId}"), HttpStatusCode.NotFound, "organization_not_found");
        await ExpectProblem(await stranger.GetAsync($"/api/v1/orgs/{orgId}/members"), HttpStatusCode.NotFound, "organization_not_found");
        await ExpectProblem(await stranger.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}", new { name = "Taken over" }), HttpStatusCode.NotFound, "organization_not_found");
        await ExpectProblem(await stranger.PostAsync($"/api/v1/orgs/{orgId}/invitations", new { email = "x@example.org", role = "admin" }), HttpStatusCode.NotFound, "organization_not_found");
        await ExpectProblem(await stranger.GetAsync($"/api/v1/orgs/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "organization_not_found");

        // Each sees only their own organisation in lists.
        Assert.Equal([strangersOwn], (await Json(await stranger.GetAsync("/api/v1/orgs"))).EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
        Assert.Equal([orgId], (await Json(await owner.GetAsync("/api/v1/orgs"))).EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client().GetAsync($"/api/v1/orgs/{orgId}")).StatusCode);
    }

    [Fact]
    public async Task An_invitation_without_smtp_gives_a_link_that_only_the_invited_address_can_accept()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var owner = await SetUpAsync(host);
        var orgId = await CreateOrgAsync(owner, "Acme");

        var invitation = await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/invitations", new { email = "Member@Example.org", role = "member" }), HttpStatusCode.Created);
        var link = invitation.GetProperty("link").GetString()!;
        Assert.StartsWith("https://localhost/invitations/", link, StringComparison.Ordinal);
        var token = link[(link.LastIndexOf('/') + 1)..];
        Assert.Equal(0L, await host.ScalarAsync<long>($"SELECT count(*) FROM iam.invitation WHERE token_hash = '{token}'"));

        // Anyone holding the link sees what it is for.
        var preview = await Json(await host.Client().GetAsync($"/api/v1/invitations/{token}"));
        Assert.Equal("Acme", preview.GetProperty("organizationName").GetString());
        Assert.Equal("member", preview.GetProperty("role").GetString());
        await ExpectProblem(await host.Client().GetAsync("/api/v1/invitations/not-a-token"), HttpStatusCode.NotFound, "invalid_invitation");

        using var other = await RegisterAsync(host, "other@example.org");
        await ExpectProblem(await other.PostAsync($"/api/v1/invitations/{token}/accept"), HttpStatusCode.Forbidden, "invitation_email_mismatch");

        using var member = await RegisterAsync(host, "member@example.org");
        var accepted = await Json(await member.PostAsync($"/api/v1/invitations/{token}/accept"));
        Assert.Equal(orgId, accepted.GetProperty("organizationId").GetGuid());
        Assert.Equal("member", Assert.Single((await Json(await member.SessionAsync())).GetProperty("organizations").EnumerateArray()).GetProperty("role").GetString());

        var members = await Json(await member.GetAsync($"/api/v1/orgs/{orgId}/members"));
        Assert.Equal(["owner", "member"], members.EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        Assert.Equal(["userId", "displayName", "email", "role", "joinedAt"], members.EnumerateArray().First().EnumerateObject().Select(p => p.Name));

        // The link is used up.
        await ExpectProblem(await member.PostAsync($"/api/v1/invitations/{token}/accept"), HttpStatusCode.NotFound, "invalid_invitation");
        Assert.Empty((await Json(await owner.GetAsync($"/api/v1/orgs/{orgId}/invitations"))).EnumerateArray());
        await ExpectProblem(await owner.PostAsync($"/api/v1/orgs/{orgId}/invitations", new { email = "member@example.org", role = "admin" }), HttpStatusCode.Conflict, "already_member");
    }

    [Fact]
    public async Task On_an_instance_open_by_invitation_only_the_link_opens_registration()
    {
        await using var host = await ApiTestHost.StartAsync(server, new() { ["REGISTRATION"] = "invite" });
        using var owner = await SetUpAsync(host);
        var orgId = await CreateOrgAsync(owner, "Acme");
        var token = await InviteAsync(owner, orgId, "new@example.org", "viewer");

        using var uninvited = host.Client();
        await ExpectProblem(await uninvited.PostAsync("/api/v1/auth/register", new { email = "else@example.org", displayName = "Else", password = ApiTestHost.Password }),
            HttpStatusCode.BadRequest, "invalid_invitation");
        // The token is for one address only.
        await ExpectProblem(await uninvited.PostAsync("/api/v1/auth/register", new { invitationToken = token, email = "else@example.org", displayName = "Else", password = ApiTestHost.Password }),
            HttpStatusCode.BadRequest, "invalid_invitation");

        using var invited = host.Client();
        Assert.Equal(HttpStatusCode.NoContent, (await invited.PostAsync("/api/v1/auth/register", new { invitationToken = token, email = "new@example.org", displayName = "New", password = ApiTestHost.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await invited.PostAsync($"/api/v1/invitations/{token}/accept")).StatusCode);
        Assert.True(await host.ScalarAsync<bool>("SELECT email_confirmed FROM iam.\"user\" WHERE normalized_email = 'NEW@EXAMPLE.ORG'"));
        Assert.Equal(HttpStatusCode.OK, (await invited.GetAsync($"/api/v1/orgs/{orgId}")).StatusCode);
    }

    [Fact]
    public async Task Roles_decide_who_invites_who_changes_roles_and_who_may_transfer_ownership()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var owner = await SetUpAsync(host);
        var orgId = await CreateOrgAsync(owner, "Acme");
        using var admin = await JoinAsync(host, owner, orgId, "admin@example.org", "admin");
        using var member = await JoinAsync(host, owner, orgId, "member@example.org", "member");
        using var viewer = await JoinAsync(host, owner, orgId, "viewer@example.org", "viewer");
        var ownerId = await UserIdAsync(host, "owner@example.org");
        var memberId = await UserIdAsync(host, "member@example.org");
        var viewerId = await UserIdAsync(host, "viewer@example.org");
        var adminId = await UserIdAsync(host, "admin@example.org");

        // Members and viewers read, but neither invite nor change settings or roles.
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/v1/orgs/{orgId}/members")).StatusCode);
        await ExpectProblem(await member.PostAsync($"/api/v1/orgs/{orgId}/invitations", new { email = "x@example.org", role = "viewer" }), HttpStatusCode.Forbidden, "insufficient_role");
        await ExpectProblem(await member.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}", new { name = "Renamed" }), HttpStatusCode.Forbidden, "insufficient_role");
        await ExpectProblem(await viewer.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}/members/{memberId}", new { role = "viewer" }), HttpStatusCode.Forbidden, "insufficient_role");
        await ExpectProblem(await member.SendAsync(HttpMethod.Delete, $"/api/v1/orgs/{orgId}/members/{viewerId}"), HttpStatusCode.Forbidden, "insufficient_role");

        // Administrators manage roles, but neither the owner's nor ownership itself.
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}/members/{viewerId}", new { role = "member" })).StatusCode);
        await ExpectProblem(await admin.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}/members/{ownerId}", new { role = "member" }), HttpStatusCode.Conflict, "owner_role");
        await ExpectProblem(await admin.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}/members/{adminId}", new { role = "owner" }), HttpStatusCode.Forbidden, "insufficient_role");
        await ExpectProblem(await admin.PostAsync($"/api/v1/orgs/{orgId}/invitations", new { email = "x@example.org", role = "owner" }), HttpStatusCode.BadRequest, "validation_failed");

        // The owner hands over and becomes an administrator; then may leave.
        await ExpectProblem(await owner.SendAsync(HttpMethod.Delete, $"/api/v1/orgs/{orgId}/members/{ownerId}"), HttpStatusCode.Conflict, "owner_role");
        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}/members/{adminId}", new { role = "owner" })).StatusCode);
        Assert.Equal("admin", await host.ScalarAsync<string>($"SELECT role FROM iam.membership WHERE user_id = '{ownerId}'"));
        Assert.Equal("owner", await host.ScalarAsync<string>($"SELECT role FROM iam.membership WHERE user_id = '{adminId}'"));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/orgs/{orgId}/members/{ownerId}")).StatusCode);
        await ExpectProblem(await owner.GetAsync($"/api/v1/orgs/{orgId}"), HttpStatusCode.NotFound, "organization_not_found");

        // Members leave on their own; the new owner removes others.
        Assert.Equal(HttpStatusCode.NoContent, (await member.SendAsync(HttpMethod.Delete, $"/api/v1/orgs/{orgId}/members/{memberId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(HttpMethod.Delete, $"/api/v1/orgs/{orgId}/members/{viewerId}")).StatusCode);
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.membership"));
        Assert.True(await host.ScalarAsync<long>("SELECT count(*) FROM platform.audit_event WHERE action IN ('org.member_role_changed', 'org.ownership_transferred', 'org.member_left', 'org.member_removed')") >= 4);
    }

    [Fact]
    public async Task With_mfa_required_members_without_it_are_sent_to_their_account_first()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var owner = await SetUpAsync(host);
        var orgId = await CreateOrgAsync(owner, "Acme");
        using var member = await JoinAsync(host, owner, orgId, "member@example.org", "member");

        await ExpectProblem(await owner.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}", new { requireMfa = true }), HttpStatusCode.Conflict, "mfa_required_for_you");
        await EnableTotpAsync(owner);
        var updated = await Json(await owner.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}", new { requireMfa = true, locale = "de", timeZone = "Europe/Zurich" }));
        Assert.True(updated.GetProperty("requireMfa").GetBoolean());

        await ExpectProblem(await member.GetAsync($"/api/v1/orgs/{orgId}/members"), HttpStatusCode.Forbidden, "mfa_required");
        Assert.True(Assert.Single((await Json(await member.SessionAsync())).GetProperty("organizations").EnumerateArray()).GetProperty("mfaRequired").GetBoolean());

        await EnableTotpAsync(member);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/v1/orgs/{orgId}/members")).StatusCode);
        await ExpectProblem(await owner.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}", new { locale = "xx" }), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task The_ai_data_policy_starts_local_only_and_only_administrators_change_it()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var owner = await SetUpAsync(host);
        var orgId = await CreateOrgAsync(owner, "Acme");
        using var member = await JoinAsync(host, owner, orgId, "member@example.org", "member");

        // Nothing leaves the operator's control until the organisation allows it (master plan 7.8).
        Assert.Equal("local_only", (await Json(await owner.GetAsync($"/api/v1/orgs/{orgId}"))).GetProperty("aiPolicy").GetString());
        await ExpectProblem(await owner.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}", new { aiPolicy = "everywhere" }), HttpStatusCode.BadRequest, "validation_failed");
        await ExpectProblem(await member.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}", new { aiPolicy = "cloud_allowed" }), HttpStatusCode.Forbidden, "insufficient_role");

        var updated = await Json(await owner.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}", new { aiPolicy = "cloud_allowed" }));
        Assert.Equal("cloud_allowed", updated.GetProperty("aiPolicy").GetString());
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.audit_event WHERE action = 'org.settings_changed' AND data->>'aiPolicy' = 'cloud_allowed'"));

        // Other modules read it through the contract, inside the organisation's unit of work.
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<Ardea.Platform.Tenancy.TenantContext>().SetOrganization(orgId);
        var profile = await scope.ServiceProvider.GetRequiredService<Ardea.Iam.Contracts.IOrganizationProfiles>().FindAsync(orgId, TestContext.Current.CancellationToken);
        Assert.Equal(("cloud_allowed", "en"), (profile!.AiPolicy, profile.Locale));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<Ardea.Iam.Contracts.IOrganizationProfiles>().FindAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task With_mfa_required_every_organisation_endpoint_refuses_a_member_without_it()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var owner = await SetUpAsync(host);
        var orgId = await CreateOrgAsync(owner, "Acme");
        using var member = await JoinAsync(host, owner, orgId, "member@example.org", "admin");
        await EnableTotpAsync(owner);
        await Expect(HttpStatusCode.OK, await owner.SendAsync(HttpMethod.Patch, $"/api/v1/orgs/{orgId}", new { requireMfa = true }));
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("/orgs/{orgId", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (Method: m, Route: e.RoutePattern.RawText!)))
            .ToList();
        Assert.NotEmpty(endpoints);

        var problems = new List<string>();
        foreach (var (method, route) in endpoints)
        {
            var path = System.Text.RegularExpressions.Regex.Replace(route, @"\{(\w+)(:(\w+))?\}", m => m.Groups[1].Value == "orgId" ? orgId.ToString() : m.Groups[3].Value switch
            {
                "guid" => Guid.CreateVersion7().ToString(),
                "int" => "1",
                _ => "x",
            });
            // A stream that starts instead of being refused must not hang the test.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await member.SendAsync(new HttpMethod(method), path, method == "GET" ? null : new { }, cancellationToken: timeout.Token);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            if (response.StatusCode != HttpStatusCode.Forbidden || !body.Contains("\"mfa_required\"", StringComparison.Ordinal))
            {
                problems.Add($"{method} {route}: {(int)response.StatusCode} {body}");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public async Task A_revoked_or_expired_invitation_can_no_longer_be_accepted()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var owner = await SetUpAsync(host);
        var orgId = await CreateOrgAsync(owner, "Acme");
        var revoked = await InviteAsync(owner, orgId, "revoked@example.org", "member");
        var expired = await InviteAsync(owner, orgId, "expired@example.org", "admin");
        var revokedId = await host.ScalarAsync<Guid>("SELECT id FROM iam.invitation WHERE normalized_email = 'REVOKED@EXAMPLE.ORG'");

        await Expect(HttpStatusCode.NoContent, await owner.SendAsync(HttpMethod.Delete, $"/api/v1/orgs/{orgId}/invitations/{revokedId}"));
        await ExpectProblem(await owner.SendAsync(HttpMethod.Delete, $"/api/v1/orgs/{orgId}/invitations/{revokedId}"), HttpStatusCode.NotFound, "invitation_not_found");
        Assert.Equal(1, await host.ScalarAsync<int>("UPDATE iam.invitation SET expires_at = now() - interval '1 minute' WHERE normalized_email = 'EXPIRED@EXAMPLE.ORG' RETURNING 1"));

        using var first = await RegisterAsync(host, "revoked@example.org");
        using var second = await RegisterAsync(host, "expired@example.org");
        await ExpectProblem(await first.PostAsync($"/api/v1/invitations/{revoked}/accept"), HttpStatusCode.NotFound, "invalid_invitation");
        await ExpectProblem(await second.PostAsync($"/api/v1/invitations/{expired}/accept"), HttpStatusCode.NotFound, "invalid_invitation");
        await ExpectProblem(await host.Client().GetAsync($"/api/v1/invitations/{expired}"), HttpStatusCode.NotFound, "invalid_invitation");
        Assert.Equal(1L, await host.ScalarAsync<long>($"SELECT count(*) FROM iam.membership WHERE organization_id = '{orgId}'"));
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

    private static async Task<string> InviteAsync(ApiClient admin, Guid orgId, string email, string role)
    {
        var link = (await Json(await admin.PostAsync($"/api/v1/orgs/{orgId}/invitations", new { email, role }), HttpStatusCode.Created)).GetProperty("link").GetString()!;
        return link[(link.LastIndexOf('/') + 1)..];
    }

    private static async Task<ApiClient> JoinAsync(ApiTestHost host, ApiClient admin, Guid orgId, string email, string role)
    {
        var token = await InviteAsync(admin, orgId, email, role);
        var client = await RegisterAsync(host, email);
        await Expect(HttpStatusCode.OK, await client.PostAsync($"/api/v1/invitations/{token}/accept"));
        return client;
    }

    private static Task<Guid> UserIdAsync(ApiTestHost host, string email) =>
        host.ScalarAsync<Guid>($"SELECT id FROM iam.\"user\" WHERE normalized_email = '{email.ToUpperInvariant()}'");

    private static async Task EnableTotpAsync(ApiClient client)
    {
        var setup = await Json(await client.PostAsync("/api/v1/account/mfa/setup", new { password = ApiTestHost.Password }));
        await Expect(HttpStatusCode.OK, await client.PostAsync("/api/v1/account/mfa", new { code = Totp.Code(setup.GetProperty("sharedKey").GetString()!) }));
    }

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
