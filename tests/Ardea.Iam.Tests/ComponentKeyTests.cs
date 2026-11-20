using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Iam.Tests;

public sealed class ComponentKeyTests(PostgresServer server)
{
    [Fact]
    public async Task Catalogue_products_are_found_by_key_cpe_and_package_and_related_products_only_weakly()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var (client, path) = await ProjectAsync(host);
        using var _ = client;

        var postgres = await AddAsync(client, path, new { productKey = "postgresql", version = "17.2" });
        var log4j = await AddAsync(client, path, new { productKey = "log4j-core", version = "2.14.1" });
        var wordpress = await AddAsync(client, path, new { productKey = "wordpress", version = "6.7.1" });

        Assert.Equal(["cpe postgresql:postgresql strong", "product postgresql strong"], await KeysAsync(host, postgres));
        var log4jKeys = await KeysAsync(host, log4j);
        Assert.Contains("product log4j-core strong", log4jKeys);
        Assert.Contains("purl pkg:maven/org.apache.logging.log4j/log4j-core strong", log4jKeys);
        Assert.Contains(log4jKeys, k => k.StartsWith("cpe ", StringComparison.Ordinal) && k.EndsWith(" strong", StringComparison.Ordinal));
        Assert.Contains("product php weak", await KeysAsync(host, wordpress));
        Assert.Contains("product wordpress strong", await KeysAsync(host, wordpress));
    }

    [Fact]
    public async Task Products_by_name_go_through_the_catalogue_and_are_weak_without_a_vendor()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var (client, path) = await ProjectAsync(host);
        using var _ = client;

        var postgres = await AddAsync(client, path, new { customName = "Postgres", kind = "database", version = "16" });
        Assert.Contains("product postgresql strong", await KeysAsync(host, postgres));

        var withVendor = await AddAsync(client, path, new { customVendor = "Acme AG", customName = "Billing Engine", kind = "other" });
        Assert.Equal(["product acme/billingengine strong"], await KeysAsync(host, withVendor));

        var nameOnly = await AddAsync(client, path, new { customName = "Billing Engine", kind = "other" });
        Assert.Equal(["product billingengine weak"], await KeysAsync(host, nameOnly));
    }

    [Fact]
    public async Task Keys_follow_the_component_through_changes_replacement_and_removal()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var (client, path) = await ProjectAsync(host);
        using var _ = client;
        var id = await AddAsync(client, path, new { productKey = "nginx", version = "1.26.2" });

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(HttpMethod.Patch, $"{path}/{id}", new { productKey = "postgresql" })).StatusCode);
        Assert.Contains("product postgresql strong", await KeysAsync(host, id));
        Assert.DoesNotContain("product nginx strong", await KeysAsync(host, id));

        var replaced = await (await client.SendAsync(HttpMethod.Put, path, new { components = new object[] { new { productKey = "php", version = "8.3.12" } } }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var php = replaced.GetProperty("components").EnumerateArray().Single().GetProperty("id").GetGuid();
        Assert.Equal(["cpe php:php strong", "product php strong"], await KeysAsync(host, php));
        Assert.Equal(0L, await host.ScalarAsync<long>($"SELECT count(*) FROM assets.component_key WHERE component_id = '{id}'"));
    }

    private static async Task<(ApiClient Client, string Path)> ProjectAsync(ApiTestHost host)
    {
        var client = host.Client();
        await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "owner@example.org", displayName = "Owner", password = ApiTestHost.Password });
        var orgId = (await (await client.PostAsync("/api/v1/orgs", new { name = "Acme" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var projectId = (await (await client.PostAsync($"/api/v1/orgs/{orgId}/projects", new { name = "Portal" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return (client, $"/api/v1/orgs/{orgId}/projects/{projectId}/components");
    }

    private static async Task<Guid> AddAsync(ApiClient client, string path, object body)
    {
        var response = await client.PostAsync(path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<List<string>> KeysAsync(ApiTestHost host, Guid component)
    {
        var joined = await host.ScalarAsync<string?>(
            $"SELECT string_agg(key_type || ' ' || key_value || ' ' || strength, '|' ORDER BY key_type, key_value) FROM assets.component_key WHERE component_id = '{component}'");
        return joined is null ? [] : [.. joined.Split('|')];
    }
}
