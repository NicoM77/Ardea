using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Iam.Tests;

/// <summary>
/// The live stream (task 3.11; master plan 10.2 «Live»): a member's stream starts with <c>ready</c> and says
/// <c>changed</c> once a change of the organisation commits; strangers get no stream.
/// </summary>
public sealed class LiveApiTests(PostgresServer server)
{
    [Fact(Timeout = 120_000)]
    public async Task A_member_hears_about_committed_changes_of_the_organisation()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var host = await ApiTestHost.StartAsync(server);
        using var client = host.Client();
        await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "owner@example.org", displayName = "Owner", password = ApiTestHost.Password });
        var org = (await (await client.PostAsync("/api/v1/orgs", new { name = "Acme" })).Content.ReadFromJsonAsync<JsonElement>(cancellation)).GetProperty("id").GetGuid();
        var other = (await (await client.PostAsync("/api/v1/orgs", new { name = "Other" })).Content.ReadFromJsonAsync<JsonElement>(cancellation)).GetProperty("id").GetGuid();

        using var response = await client.StreamAsync($"/api/v1/orgs/{org}/events", cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellation));
        Assert.Equal("ready", await NextEventAsync(reader));

        // The stream holds no transaction while it waits (master plan 9.2).
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM pg_stat_activity WHERE state LIKE 'idle in transaction%' AND usename = 'ardea_app' AND datname = current_database()"));

        // A change of another organisation says nothing here; one of this organisation does, once committed.
        await host.ScalarAsync<int>($"INSERT INTO assess.assess_event (id, organization_id, at, kind, data) VALUES (gen_random_uuid(), '{other}', now(), 'action.raised', '{{}}') RETURNING 1");
        await host.ScalarAsync<int>($"INSERT INTO assess.assess_event (id, organization_id, at, kind, data) VALUES (gen_random_uuid(), '{org}', now(), 'action.raised', '{{}}') RETURNING 1");
        Assert.Equal("changed", await NextEventAsync(reader));
    }

    [Fact]
    public async Task Someone_outside_the_organisation_gets_no_stream()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var owner = host.Client();
        await owner.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "owner@example.org", displayName = "Owner", password = ApiTestHost.Password });
        var org = (await (await owner.PostAsync("/api/v1/orgs", new { name = "Acme" })).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();

        using var stranger = host.Client();
        using var anonymous = await stranger.StreamAsync($"/api/v1/orgs/{org}/events", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    /// <summary>The type of the next event; a «ping» is skipped.</summary>
    private static async Task<string> NextEventAsync(StreamReader reader)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal) && line[7..] is var kind && kind != "ping")
            {
                return kind;
            }
        }

        throw new InvalidOperationException("The stream ended.");
    }
}
