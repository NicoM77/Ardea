using System.Net;
using Ardea.TestSupport;

namespace Ardea.Notifications.Tests;

public sealed class DeliveryLogTests(PostgresServer server)
{
    [Fact]
    public async Task The_delivery_log_lists_each_message_with_its_recipient_and_events_by_state_and_channel_page_by_page_for_admins_only()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        var address = await host.ScalarAsync<Guid>($"""
            INSERT INTO notify.external_address (organization_id, address, normalized_address, created_at, confirmed_at)
            VALUES ('{s.Organization}', 'oncall@partner.example', 'ONCALL@PARTNER.EXAMPLE', now(), now()) RETURNING id
            """);
        await s.RuleAsync("All", 2, ["action.raised"], Scenario.Target("in_app"), Scenario.Target("email"), Scenario.Target("email", kind: "address", reference: address.ToString()));
        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 4));
        await s.EventAsync("action.raised", await s.ActionAsync(3, level: 5));
        await s.EvaluateAsync();
        var log = $"{s.Path}/deliveries";

        var all = (await NotifyTestHost.JsonAsync(await s.Owner.GetAsync(log))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(
            ["email address oncall@partner.example pending 2", "email user Owner pending 2", "in_app user Owner sent 1", "in_app user Owner sent 1"],
            all.Select(d => $"{d.GetProperty("channel")} {d.GetProperty("recipient").GetProperty("kind")} {d.GetProperty("recipient").GetProperty("label")} {d.GetProperty("state")} {d.GetProperty("events")}").Order());
        Assert.Equal(all.Select(d => d.GetProperty("createdAt").GetDateTimeOffset()).OrderDescending(), all.Select(d => d.GetProperty("createdAt").GetDateTimeOffset()));

        async Task<List<string?>> ChannelsAsync(string query) =>
            [.. (await NotifyTestHost.JsonAsync(await s.Owner.GetAsync($"{log}?{query}"))).GetProperty("items").EnumerateArray().Select(d => d.GetProperty("channel").GetString())];

        Assert.Equal(["email", "email"], await ChannelsAsync("channel=email"));
        Assert.Equal(["in_app", "in_app"], await ChannelsAsync("state=sent"));
        Assert.Empty(await ChannelsAsync("state=sent&channel=email"));

        var page = await NotifyTestHost.JsonAsync(await s.Owner.GetAsync($"{log}?limit=3"));
        Assert.Equal(3, page.GetProperty("items").GetArrayLength());
        var rest = await NotifyTestHost.JsonAsync(await s.Owner.GetAsync($"{log}?limit=3&cursor={page.GetProperty("nextCursor").GetString()}"));
        Assert.Equal(1, rest.GetProperty("items").GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, rest.GetProperty("nextCursor").ValueKind);

        foreach (var query in new[] { "state=lost", "channel=sms", "limit=0", "limit=201", "cursor=next", "cursor=-1" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.GetAsync($"{log}?{query}")).StatusCode);
        }

        using var member = await host.RegisterAsync("member@example.org", "Mia Member");
        await host.AddMemberAsync(s.Organization, "member@example.org", "member");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(log)).StatusCode);
    }
}
