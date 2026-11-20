using Ardea.Notifications.Delivering;
using Ardea.Notifications.Evaluation;
using Ardea.TestSupport;

namespace Ardea.Notifications.Tests;

public sealed class HourlyCapTests(PostgresServer server)
{
    [Fact]
    public async Task A_message_above_the_hourly_cap_reaches_a_recipient_without_a_digest_target_on_its_channel_an_hour_later()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await host.ExecuteAsync("DELETE FROM platform.job");
        await s.RuleAsync("Critical", 5, ["action.raised"], Scenario.Target("email"));
        await FillTheHourAsync(host, s, host.Time.GetUtcNow().AddMinutes(-10));
        var owner = await s.OwnerIdAsync();
        var action = await s.ActionAsync(2, level: 5);

        await s.EventAsync("action.raised", action);
        await s.EvaluateAsync();
        Assert.Equal(
            $"suppressed|{Evaluator.CappedReason}",
            await host.ScalarAsync<string>($"SELECT state || '|' || last_error FROM notify.delivery WHERE recipient_key = 'user:{owner}'"));

        host.Time.Advance(Evaluator.LimitWindow - TimeSpan.FromMinutes(1));
        Assert.Empty(await host.RunJobsAsync());
        Assert.Empty(host.Mail!.Sent);

        host.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Empty(await host.RunJobsAsync());
        var mail = Assert.Single(host.Mail.Sent);
        Assert.Equal("owner@example.org", mail.To);
        Assert.StartsWith("[5 Critical] #2 PostgreSQL in Platform Ops", mail.Subject, StringComparison.Ordinal);
        Assert.Equal(
            ["sent action.raised"],
            await host.RowsAsync($"SELECT d.state || ' ' || e.event FROM notify.delivery_event e JOIN notify.delivery d ON d.id = e.delivery_id WHERE e.recipient_key = 'user:{owner}'"));
        Assert.Equal(5, await host.ScalarAsync<int>($"SELECT last_level FROM notify.recipient_state WHERE remediation_id = '{action}'"));
    }

    [Fact]
    public async Task Rises_above_the_hourly_cap_go_out_together_in_the_overflow_and_a_rise_after_it_is_not_swallowed()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await host.ExecuteAsync("DELETE FROM platform.job");
        await s.RuleAsync("Medium", 3, ["action.raised", "action.level_raised"], Scenario.Target("email"));
        await FillTheHourAsync(host, s, host.Time.GetUtcNow().AddMinutes(-10));
        var action = await s.ActionAsync(2, level: 3);

        await s.EventAsync("action.raised", action);
        await s.EvaluateAsync();
        host.Time.Advance(TimeSpan.FromMinutes(20));
        await s.SetActionAsync(action, 4);
        await s.EventAsync("action.level_raised", action, """{"from": 3, "to": 4}""");
        await s.EvaluateAsync();
        Assert.Equal(2L, await s.CountAsync("SELECT count(*) FROM notify.delivery WHERE state = 'suppressed'"));

        host.Time.Advance(TimeSpan.FromMinutes(40));
        Assert.Empty(await host.RunJobsAsync());
        var overflow = Assert.Single(host.Mail!.Sent);
        Assert.StartsWith("[4 High] #2 PostgreSQL in Platform Ops", overflow.Subject, StringComparison.Ordinal);
        Assert.Equal(
            ["action.raised", "action.level_raised"],
            await host.RowsAsync("SELECT e.event FROM notify.delivery_event e JOIN notify.delivery d ON d.id = e.delivery_id WHERE d.state = 'sent' ORDER BY e.occurred_at"));

        await s.SetActionAsync(action, 5);
        await s.EventAsync("action.level_raised", action, """{"from": 4, "to": 5}""");
        await s.EvaluateAsync();
        await s.DeliverAsync();
        Assert.Equal(2, host.Mail.Sent.Count);
        Assert.StartsWith("[5 Critical] #2 PostgreSQL in Platform Ops", host.Mail.Sent[1].Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_message_above_the_hourly_cap_goes_out_once_whether_the_digest_of_its_channel_or_the_overflow_comes_first()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await host.ExecuteAsync("DELETE FROM platform.job");
        var rule = await s.RuleAsync("High", 4, ["action.raised"], Scenario.Target("email"), Scenario.Target("email", "digest", cron: "0 8 * * *"));
        var target = rule.GetProperty("targets").EnumerateArray().Single(t => t.GetProperty("mode").GetString() == "digest").GetProperty("id").GetGuid();

        async Task DigestDueInAsync(TimeSpan after)
        {
            await host.ExecuteAsync($"UPDATE notify.rule_target SET next_digest_at = date_trunc('second', '{host.Time.GetUtcNow() + after:O}'::timestamptz) WHERE id = '{target}'");
            host.Time.Advance(after);
            await host.RunAsync(new FindDueDigests(), organization: null);
            Assert.Empty(await host.RunJobsAsync());
        }

        await FillTheHourAsync(host, s, host.Time.GetUtcNow().AddMinutes(-10));
        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 4));
        await s.EvaluateAsync();
        await DigestDueInAsync(TimeSpan.FromMinutes(30));
        var digest = Assert.Single(host.Mail!.Sent);
        Assert.Contains("#2 PostgreSQL in Platform Ops", digest.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("(already reported)", digest.Text, StringComparison.Ordinal);

        host.Time.Advance(TimeSpan.FromMinutes(30));
        Assert.Empty(await host.RunJobsAsync());
        Assert.Single(host.Mail.Sent);
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.delivery WHERE kind = 'immediate' AND state <> 'suppressed' AND recipient_key LIKE 'user:%'"));

        await FillTheHourAsync(host, s, host.Time.GetUtcNow().AddMinutes(-10));
        await s.EventAsync("action.raised", await s.ActionAsync(3, level: 4));
        await s.EvaluateAsync();
        host.Time.Advance(Evaluator.LimitWindow);
        Assert.Empty(await host.RunJobsAsync());
        Assert.Equal(2, host.Mail.Sent.Count);
        Assert.StartsWith("[4 High] #3 PostgreSQL in Platform Ops", host.Mail.Sent[1].Subject, StringComparison.Ordinal);

        await DigestDueInAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(3, host.Mail.Sent.Count);
        Assert.Contains("#3 PostgreSQL in Platform Ops", host.Mail.Sent[2].Text, StringComparison.Ordinal);
        Assert.Contains("(already reported)", host.Mail.Sent[2].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("#2 PostgreSQL", host.Mail.Sent[2].Text, StringComparison.Ordinal);
    }

    private static Task FillTheHourAsync(NotifyTestHost host, Scenario s, DateTimeOffset at) => host.ExecuteAsync($"""
        INSERT INTO notify.delivery (id, organization_id, kind, channel, recipient_key, send_after, state, attempts, created_at)
        SELECT gen_random_uuid(), '{s.Organization}', 'immediate', 'email', 'address:' || gen_random_uuid(), '{at:O}', 'sent', 1, '{at:O}'
        FROM generate_series(1, {Evaluator.HourlyLimit})
        """);
}
