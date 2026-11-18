using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

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
