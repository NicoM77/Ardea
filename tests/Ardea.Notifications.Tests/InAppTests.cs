using Ardea.TestSupport;
using Npgsql;

namespace Ardea.Notifications.Tests;

public sealed class InAppTests(PostgresServer server)
{
    [Fact(Timeout = 120_000)]
    public async Task A_new_notification_wakes_the_live_stream_and_reading_it_lowers_only_the_own_counter()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        using var viewer = await host.RegisterAsync("viewer@example.org", "Vera Viewer");
        await host.AddMemberAsync(s.Organization, "viewer@example.org", "viewer");
        await s.RuleAsync("Everyone", 3, ["action.raised"], Scenario.Target("in_app", reference: "viewer"));

        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 4));
        using var stream = await s.Owner.StreamAsync($"{s.Path}/events", cancellation);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(cancellation));
        Assert.Equal("ready", await NextEventAsync(reader));

        await s.EvaluateAsync();
        Assert.Equal("changed", await NextEventAsync(reader));

        var mine = await NotifyTestHost.JsonAsync(await s.Owner.GetAsync($"{s.Path}/notifications"));
        Assert.Equal(1, mine.GetProperty("unread").GetInt32());
        var item = Assert.Single(mine.GetProperty("items").EnumerateArray());
        Assert.Equal("[4 High] #2 PostgreSQL in Platform Ops", item.GetProperty("title").GetString());
        Assert.Equal("/o/acme/reminders/2", item.GetProperty("link").GetString());
        var theirs = await NotifyTestHost.JsonAsync(await viewer.GetAsync($"{s.Path}/notifications"));
        Assert.NotEqual(item.GetProperty("id").GetGuid(), theirs.GetProperty("items")[0].GetProperty("id").GetGuid());

        var foreign = await NotifyTestHost.JsonAsync(await viewer.PostAsync($"{s.Path}/notifications/read", new { ids = new[] { item.GetProperty("id").GetGuid() } }));
        Assert.Equal(1, foreign.GetProperty("unread").GetInt32());
        Assert.Equal(1, (await NotifyTestHost.JsonAsync(await s.Owner.GetAsync($"{s.Path}/notifications/unread-count"))).GetProperty("unread").GetInt32());
        var read = await NotifyTestHost.JsonAsync(await s.Owner.PostAsync($"{s.Path}/notifications/read", new { all = true }));
        Assert.Equal(0, read.GetProperty("unread").GetInt32());
        Assert.Equal(1, (await NotifyTestHost.JsonAsync(await viewer.GetAsync($"{s.Path}/notifications/unread-count"))).GetProperty("unread").GetInt32());
        Assert.Empty((await NotifyTestHost.JsonAsync(await s.Owner.GetAsync($"{s.Path}/notifications?unread=true"))).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Without_a_user_the_application_role_reads_no_notification_and_writes_none_for_a_stranger()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("Everyone", 3, ["action.raised"], Scenario.Target("in_app"));
        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 4));
        await s.EvaluateAsync();
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.notification"));

        await using var dataSource = NpgsqlDataSource.Create(host.Database.AppConnectionString("tests"));
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand($"SELECT set_config('ardea.org_id', '{s.Organization}', true)", connection, transaction))
        {
            await set.ExecuteNonQueryAsync();
        }

        await using (var count = new NpgsqlCommand("SELECT count(*) FROM notify.notification", connection, transaction))
        {
            Assert.Equal(0L, await count.ExecuteScalarAsync());
        }

        await using var insert = new NpgsqlCommand(
            $"INSERT INTO notify.notification (user_id, organization_id, event_id, kind, title, body, created_at) VALUES (gen_random_uuid(), '{s.Organization}', gen_random_uuid(), 'x', 'x', 'x', now())",
            connection, transaction);
        var refused = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
    }

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
