using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ardea.Host.Commands;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ardea.Iam.Tests;

public sealed class OrgCommandTests(PostgresServer server)
{
    [Fact]
    public async Task Export_holds_one_organisation_without_secrets_and_delete_removes_exactly_it()
    {
        await using var host = await ApiTestHost.StartAsync(server,
            services: s => s.AddHttpClient(Ardea.Assets.GitHub.GitHubApi.ClientName).ConfigurePrimaryHttpMessageHandler(() => new TokenAccepted()));
        using var client = host.Client();
        await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "owner@example.org", displayName = "Owner Ünïcode", password = ApiTestHost.Password });
        var acme = await SeedAsync(client, "Acme", withToken: true);
        var other = await SeedAsync(client, "Other", withToken: false);

        await using var owner = new NpgsqlConnection(host.Database.OwnerConnectionString("tests"));
        await owner.OpenAsync(TestContext.Current.CancellationToken);

        var text = new StringWriter();
        Assert.True(await OrgCommand.ExportAsync(owner, "acme", text, TestContext.Current.CancellationToken));
        var export = JsonDocument.Parse(text.ToString()).RootElement;
        Assert.Equal(OrgCommand.Format, export.GetProperty("format").GetString());
        Assert.Equal("Acme", export.GetProperty("organization").GetProperty("name").GetString());
        Assert.Equal("Owner Ünïcode", export.GetProperty("members")[0].GetProperty("displayName").GetString());
        var tables = export.GetProperty("tables");
        Assert.Equal(2, tables.GetProperty("assets.project").GetArrayLength());
        Assert.Equal(2, tables.GetProperty("assets.component").GetArrayLength());
        Assert.Equal(1, tables.GetProperty("iam.invitation").GetArrayLength());
        Assert.DoesNotContain(other.ToString(), text.ToString(), StringComparison.Ordinal);
        Assert.False(tables.GetProperty("assets.repository_connection")[0].TryGetProperty("token_enc", out _));
        Assert.DoesNotContain("password", text.ToString(), StringComparison.OrdinalIgnoreCase);

        var deleted = await OrgCommand.DeleteAsync(owner, "acme", TestContext.Current.CancellationToken);
        Assert.NotNull(deleted);
        Assert.Equal(1, deleted["iam.organization"]);
        foreach (var table in new[] { "assets.component", "assets.component_key", "assets.project", "assets.repository_connection", "iam.membership", "iam.invitation", "platform.audit_event" })
        {
            Assert.Equal(0L, await host.ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE organization_id = '{acme}'"));
        }

        Assert.Equal(2L, await host.ScalarAsync<long>($"SELECT count(*) FROM assets.project WHERE organization_id = '{other}'"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.\"user\""));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.audit_event WHERE action = 'instance.organization_deleted' AND organization_id IS NULL"));
        Assert.Null(await OrgCommand.DeleteAsync(owner, "acme", TestContext.Current.CancellationToken));
    }

    private static async Task<Guid> SeedAsync(ApiClient client, string name, bool withToken)
    {
        var orgId = (await (await client.PostAsync("/api/v1/orgs", new { name })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var projectId = (await (await client.PostAsync($"/api/v1/orgs/{orgId}/projects", new { name = "Portal" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await client.SendAsync(HttpMethod.Put, $"/api/v1/orgs/{orgId}/projects/{projectId}/components",
            new { components = new object[] { new { productKey = "postgresql", version = "17.2" }, new { productKey = "php", version = "8.3.12" } } });
        await client.PostAsync($"/api/v1/orgs/{orgId}/invitations", new { email = $"x@{name.ToLowerInvariant()}.example", role = "viewer" });
        var connection = await client.PostAsync($"/api/v1/orgs/{orgId}/repository-connections", new { name = "GitHub", token = withToken ? "github_pat_secret0123456789" : null });
        Assert.Equal(HttpStatusCode.Created, connection.StatusCode);
        return orgId;
    }

    private sealed class TokenAccepted : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"login":"bot"}""", Encoding.UTF8, "application/json") });
    }
}
