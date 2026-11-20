using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Ardea.Host.Commands;
using Ardea.TestSupport;
using Microsoft.Extensions.Configuration;

namespace Ardea.Iam.Tests;

public sealed class SeedDemoDeterminismTests(PostgresServer server)
{
    private const string ReminderNumbers = """
        SELECT coalesce(string_agg(r.number || ' ' || coalesce(c.product_key, '-') || ' L' || r.level || ' ' || r.status || ' ' || r.open_findings, E'\n' ORDER BY r.number), '')
        FROM assess.remediation r JOIN assets.component c ON c.id = r.component_id
        """;

    private const string NoticeOrder = """
        SELECT coalesce(string_agg(n.entry_key || ' ' || n.state, E'\n' ORDER BY n.exploited DESC, n.score_rank DESC, n.raised_at DESC, n.id DESC), '')
        FROM assess.notice n
        """;

    private const string Counts = """
        SELECT 'findings ' || (SELECT count(*) FROM assess.finding)
            || ', alerts ' || (SELECT count(*) FROM assess.alert)
            || ', events ' || (SELECT count(*) FROM assess.assess_event)
            || ', recent action events ' || (SELECT count(*) FROM assess.assess_event WHERE kind LIKE 'action.%' AND at >= now() - interval '1 day')
            || ', entries ' || (SELECT count(*) FROM intel.security_entry)
            || ', notifications ' || (SELECT count(*) FROM notify.notification)
            || ', deliveries ' || (SELECT count(*) FROM notify.delivery)
            || ', notification kinds ' || (SELECT coalesce(string_agg(kind || ' ' || level || ' ' || n, '; ' ORDER BY kind, level), '') FROM (SELECT kind, coalesce(level, 0) AS level, count(*) AS n FROM notify.notification GROUP BY 1, 2) k)
        """;

    [Fact]
    public async Task Two_seeds_on_two_fresh_databases_give_the_same_reminders_notices_and_counts()
    {
        var first = await SeedAndDescribeAsync();
        var second = await SeedAndDescribeAsync();
        Assert.Equal(first, second);
        Assert.Contains("CVE-", first, StringComparison.Ordinal);
    }

    private async Task<string> SeedAndDescribeAsync()
    {
        var masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var host = await ApiTestHost.StartAsync(server, new Dictionary<string, string?> { ["MASTER_KEY"] = masterKey });
        await using var services = CommandServices.Build(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_HOST"] = host.Database.Host,
                ["DB_PORT"] = host.Database.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["DB_NAME"] = host.Database.Database,
                ["DB_OWNER_USER"] = host.Database.OwnerUser,
                ["DB_OWNER_PASSWORD"] = host.Database.OwnerPassword,
                ["DB_APP_PASSWORD"] = host.Database.AppPassword,
                ["MASTER_KEY"] = masterKey,
                ["PUBLIC_URL"] = "https://localhost",
                ["PASSWORD_HASH_ITERATIONS"] = "1000",
                ["SMTP:HOST"] = "smtp.invalid",
                ["SMTP:FROM"] = "ardea@example.org",
            }).Build(),
            "seed-demo",
            Microsoft.Extensions.Logging.LogLevel.Warning);
        var output = new StringWriter();
        Assert.Equal(0, await SeedDemoCommand.SeedAsync(services, output, TextWriter.Null, TestContext.Current.CancellationToken));
        var password = output.ToString().Split("password  ")[1].Split(Environment.NewLine)[0].Trim();
        using var client = host.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/login", new { email = "nina.meier@example.org", password })).StatusCode);
        var session = await (await client.SessionAsync()).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var path = $"/api/v1/orgs/{session.GetProperty("organizations")[0].GetProperty("id").GetGuid()}";
        return string.Join(
            "\n--\n",
            await host.ScalarAsync<string>(ReminderNumbers),
            await host.ScalarAsync<string>(NoticeOrder),
            await host.ScalarAsync<string>(Counts),
            await ShownRemindersAsync(client, path),
            await ShownNoticesAsync(client, path),
            await ShownOverviewAsync(client, path));
    }

    private static async Task<string> ShownRemindersAsync(ApiClient client, string path)
    {
        var page = await (await client.GetAsync($"{path}/actions?status=all")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var lines = new List<string>();
        foreach (var item in page.GetProperty("items").EnumerateArray())
        {
            var number = item.GetProperty("number").GetInt32();
            var detail = await (await client.GetAsync($"{path}/actions/{number}")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            var keys = string.Join(",", item.GetProperty("entries").EnumerateArray().Select(k => k.GetString()));
            var lead = detail.GetProperty("answers").GetProperty("whatHappened").GetProperty("entryKey").GetString();
            var findings = string.Join(",", detail.GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("entry").GetProperty("key").GetString()));
            lines.Add($"#{number} L{item.GetProperty("level").GetInt32()} keys {keys} lead {lead} findings {findings}");
        }

        return string.Join('\n', lines);
    }

    private static async Task<string> ShownNoticesAsync(ApiClient client, string path)
    {
        var lines = new List<string>();
        foreach (var state in new[] { "new", "earlier", "answered" })
        {
            var page = await (await client.GetAsync($"{path}/notices?state={state}")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            lines.Add($"{state}: {string.Join(",", page.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("entry").GetProperty("key").GetString()))}");
        }

        return string.Join('\n', lines);
    }

    private static async Task<string> ShownOverviewAsync(ApiClient client, string path)
    {
        var overview = await (await client.GetAsync($"{path}/overview")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var urgent = string.Join(",", overview.GetProperty("urgent").EnumerateArray().Select(u => u.GetProperty("action").GetProperty("number").GetInt32()));
        var changes = string.Join(", ", overview.GetProperty("last24Hours").EnumerateArray().Select(c => $"{c.GetProperty("kind").GetString()} {c.GetProperty("count").GetInt32()}"));
        return $"urgent {urgent}; since yesterday {changes}";
    }
}
