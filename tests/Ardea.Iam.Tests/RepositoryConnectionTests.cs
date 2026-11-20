using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ardea.Assets.GitHub;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Iam.Tests;

public sealed class RepositoryConnectionTests(PostgresServer server)
{
    private const string FineGrained = "github_pat_11AAAAAA_fineGrainedTokenForTests1234";
    private const string Classic = "ghp_classicTokenForTestsWXYZ";

    [Fact]
    public async Task A_fine_grained_token_is_checked_stored_encrypted_and_never_shown_again()
    {
        var github = new FakeGitHub();
        await using var host = await StartAsync(github);
        var (owner, orgId) = await OrgAsync(host);
        using var _ = owner;

        var created = await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/repository-connections", new { name = "Acme GitHub", token = FineGrained }), HttpStatusCode.Created);
        Assert.Equal("fine_grained", created.GetProperty("tokenKind").GetString());
        Assert.Equal("1234", created.GetProperty("tokenHint").GetString());
        Assert.Equal("acme-bot", created.GetProperty("login").GetString());
        Assert.Equal(new DateTimeOffset(2027, 1, 31, 0, 0, 0, TimeSpan.Zero), created.GetProperty("tokenExpiresAt").GetDateTimeOffset());
        Assert.Empty(created.GetProperty("warnings").EnumerateArray());
        Assert.DoesNotContain(FineGrained, created.GetRawText(), StringComparison.Ordinal);

        var stored = await host.ScalarAsync<string>("SELECT token_enc FROM assets.repository_connection");
        Assert.DoesNotContain(FineGrained, stored, StringComparison.Ordinal);
        Assert.DoesNotContain(FineGrained, await (await owner.GetAsync($"/api/v1/orgs/{orgId}/repository-connections")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0L, await host.ScalarAsync<long>($"SELECT count(*) FROM platform.audit_event WHERE data::text LIKE '%{FineGrained}%'"));

        Assert.All(github.Requests, r => Assert.Equal("api.github.com", r.Host));
        Assert.All(github.Requests, r => Assert.Equal(GitHubApi.ApiVersion, r.ApiVersion));

        var id = created.GetProperty("id").GetGuid();
        var repositories = await Json(await owner.GetAsync($"/api/v1/orgs/{orgId}/repository-connections/{id}/repositories?q=portal"));
        Assert.Equal(["acme/portal"], repositories.EnumerateArray().Select(r => r.GetProperty("owner").GetString() + "/" + r.GetProperty("name").GetString()));
        Assert.True((await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/repository-connections/{id}/test"))).TryGetProperty("lastOkAt", out var ok) && ok.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task A_refused_token_is_not_stored_and_a_classic_one_is_taken_with_a_warning()
    {
        await using var host = await StartAsync(new FakeGitHub());
        var (owner, orgId) = await OrgAsync(host);
        using var _ = owner;

        var refused = await owner.PostAsync($"/api/v1/orgs/{orgId}/repository-connections", new { name = "Wrong", token = "github_pat_revoked" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("invalid_token", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM assets.repository_connection"));

        var classic = await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/repository-connections", new { name = "Old", token = Classic }), HttpStatusCode.Created);
        Assert.Equal("classic", classic.GetProperty("tokenKind").GetString());
        var warnings = classic.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToList();
        Assert.Contains(warnings, w => w.Contains("admin:org", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("does not expire", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_a_token_only_a_public_repository_named_exactly_is_found()
    {
        await using var host = await StartAsync(new FakeGitHub());
        var (owner, orgId) = await OrgAsync(host);
        using var _ = owner;
        var id = (await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/repository-connections", new { name = "Public" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        Assert.Equal(["expressjs/express"], (await Json(await owner.GetAsync($"/api/v1/orgs/{orgId}/repository-connections/{id}/repositories?q=expressjs/express")))
            .EnumerateArray().Select(r => r.GetProperty("owner").GetString() + "/" + r.GetProperty("name").GetString()));
        Assert.Empty((await Json(await owner.GetAsync($"/api/v1/orgs/{orgId}/repository-connections/{id}/repositories?q=express"))).EnumerateArray());
    }

    [Fact]
    public async Task Members_choose_from_accesses_but_only_administrators_add_them()
    {
        await using var host = await StartAsync(new FakeGitHub());
        var (owner, orgId) = await OrgAsync(host);
        using var _ = owner;
        var link = (await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/invitations", new { email = "dev@example.org", role = "member" }), HttpStatusCode.Created)).GetProperty("link").GetString()!;
        using var member = host.Client();
        await member.PostAsync("/api/v1/auth/register", new { email = "dev@example.org", displayName = "Dev", password = ApiTestHost.Password });
        await member.PostAsync($"/api/v1/invitations/{link[(link.LastIndexOf('/') + 1)..]}/accept");
        var id = (await Json(await owner.PostAsync($"/api/v1/orgs/{orgId}/repository-connections", new { name = "Acme", token = FineGrained }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync($"/api/v1/orgs/{orgId}/repository-connections", new { name = "Mine", token = FineGrained })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.SendAsync(HttpMethod.Delete, $"/api/v1/orgs/{orgId}/repository-connections/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/v1/orgs/{orgId}/repository-connections/{id}/repositories")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/orgs/{orgId}/repository-connections/{id}")).StatusCode);
    }

    [Fact]
    public async Task Only_the_instance_administrator_registers_enterprise_servers_and_never_internal_metadata_addresses()
    {
        await using var host = await StartAsync(new FakeGitHub());
        var (admin, orgId) = await OrgAsync(host);
        using var _ = admin;

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/instance/github-hosts", new { name = "Loop", apiUrl = "https://127.0.0.1/api/v3/" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/instance/github-hosts", new { name = "Metadata", apiUrl = "https://169.254.169.254/" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/instance/github-hosts", new { name = "Plain", apiUrl = "http://10.1.2.3/api/v3/" })).StatusCode);
        var ghes = await Json(await admin.PostAsync("/api/v1/instance/github-hosts", new { name = "Acme GHES", apiUrl = "https://10.1.2.3/api/v3" }), HttpStatusCode.Created);
        Assert.Equal("https://10.1.2.3/api/v3/", ghes.GetProperty("apiUrl").GetString());

        var hostId = ghes.GetProperty("id").GetString();
        var connection = await Json(await admin.PostAsync($"/api/v1/orgs/{orgId}/repository-connections", new { name = "Internal", hostId }), HttpStatusCode.Created);
        Assert.Equal("Acme GHES", connection.GetProperty("hostName").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/v1/orgs/{orgId}/repository-connections", new { name = "Unknown", hostId = "nope" })).StatusCode);

        using var stranger = host.Client();
        await stranger.PostAsync("/api/v1/auth/register", new { email = "x@example.org", displayName = "X", password = ApiTestHost.Password });
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync("/api/v1/instance/github-hosts", new { name = "Mine", apiUrl = "https://10.9.9.9/" })).StatusCode);
        Assert.Single((await Json(await stranger.GetAsync("/api/v1/github-hosts"))).EnumerateArray());
    }

    private Task<ApiTestHost> StartAsync(FakeGitHub github) =>
        ApiTestHost.StartAsync(server, services: services => services.AddHttpClient(GitHubApi.ClientName).ConfigurePrimaryHttpMessageHandler(() => github));

    private static async Task<(ApiClient Client, Guid OrgId)> OrgAsync(ApiTestHost host)
    {
        var client = host.Client();
        await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "owner@example.org", displayName = "Owner", password = ApiTestHost.Password });
        return (client, (await Json(await client.PostAsync("/api/v1/orgs", new { name = "Acme" }), HttpStatusCode.Created)).GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        if (response.StatusCode != status)
        {
            Assert.Fail($"Expected {(int)status}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public List<(string Host, string? ApiVersion)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            lock (Requests)
            {
                Requests.Add((uri.Host, request.Headers.TryGetValues("X-GitHub-Api-Version", out var v) ? v.First() : null));
            }

            var token = request.Headers.Authorization?.Parameter;
            return Task.FromResult(uri.AbsolutePath switch
            {
                "/user" when token == FineGrained => Answer("""{"login":"acme-bot"}""", ("github-authentication-token-expiration", "2027-01-31 00:00:00 UTC")),
                "/user" when token == Classic => Answer("""{"login":"old-admin"}""", ("X-OAuth-Scopes", "repo, admin:org")),
                "/user" => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                "/user/repos" when token is not null => Answer("""
                    [{"name":"portal","owner":{"login":"acme"},"private":true,"default_branch":"main","pushed_at":"2026-11-18T10:00:00Z"},
                     {"name":"website","owner":{"login":"acme"},"private":false,"default_branch":"main","pushed_at":"2026-11-17T10:00:00Z"}]
                    """),
                "/repos/expressjs/express" => Answer("""{"name":"express","owner":{"login":"expressjs"},"private":false,"default_branch":"master"}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        }

        private static HttpResponseMessage Answer(string json, params (string Name, string Value)[] headers)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        }
    }
}
