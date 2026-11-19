using Ardea.TestSupport;

namespace Ardea.Notifications.Tests;

/// <summary>The system warns about failing deliveries and shows their figures to the administrator (master plan 12.7; task 6.6).</summary>
public sealed class StatusTests(PostgresServer server)
{
    [Fact]
    public async Task Dead_deliveries_of_the_last_day_warn_the_administrator_and_old_ones_do_not()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);

        var quiet = await NotifyTestHost.JsonAsync(await s.Owner.GetAsync("/api/v1/instance/status"));
        Assert.DoesNotContain(quiet.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "deliveries_failing");

        // A dead delivery from three days ago is history; a sent one and a retrying one are only figures.
        await Insert(host, s, "dead", "now() - interval '3 days'", "a");
        await Insert(host, s, "sent", "now() - interval '1 hour'", "b");
        await Insert(host, s, "failed", "now() - interval '2 hours'", "c");
        var calm = await NotifyTestHost.JsonAsync(await s.Owner.GetAsync("/api/v1/instance/status"));
        Assert.DoesNotContain(calm.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "deliveries_failing");
        Assert.Equal(
            ["failed=1", "sent=1"],
            Figures(calm, "deliveries"));

        await Insert(host, s, "dead", "now() - interval '1 hour'", "d");
        var warned = await NotifyTestHost.JsonAsync(await s.Owner.GetAsync("/api/v1/instance/status"));
        var warning = Assert.Single(warned.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "deliveries_failing");
        Assert.Contains("1 message(s)", warning.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(["dead=1", "failed=1", "sent=1"], Figures(warned, "deliveries"));

        // Every signed-in user sees the warning on the overview; the figures stay with the administrator.
        var overview = await NotifyTestHost.JsonAsync(await s.Owner.GetAsync("/api/v1/status"));
        Assert.Contains(overview.EnumerateArray(), w => w.GetProperty("code").GetString() == "deliveries_failing");
    }

    private static string[] Figures(System.Text.Json.JsonElement status, string group) =>
        [.. status.GetProperty("metrics").EnumerateArray().Where(m => m.GetProperty("group").GetString() == group)
            .Select(m => $"{m.GetProperty("name").GetString()}={m.GetProperty("value").GetDouble():0}").Order(StringComparer.Ordinal)];

    private static Task Insert(NotifyTestHost host, Scenario s, string state, string createdAt, string recipient) =>
        host.ExecuteAsync($"""
            INSERT INTO notify.delivery (organization_id, kind, channel, recipient_key, send_after, state, attempts, created_at)
            VALUES ('{s.Organization}', 'immediate', 'webhook', '{recipient}', {createdAt}, '{state}', 1, {createdAt})
            """);
}
