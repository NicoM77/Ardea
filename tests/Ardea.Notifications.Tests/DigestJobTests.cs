using System.Net;
using Ardea.Notifications.Delivering;
using Ardea.TestSupport;

namespace Ardea.Notifications.Tests;

public sealed class DigestJobTests(PostgresServer server)
{
    [Fact]
    public async Task A_digest_goes_out_once_per_period_not_to_a_muted_member_and_after_an_outage_once_for_the_whole_gap()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        using (var member = await host.RegisterAsync("member@example.org", "Mia Member"))
        {
            await host.AddMemberAsync(s.Organization, "member@example.org", "member");
            Assert.Equal(HttpStatusCode.OK, (await member.PutAsync($"{s.Path}/notification-preferences", new { mutedChannels = new[] { "email" } })).StatusCode);
        }

        await host.ExecuteAsync("DELETE FROM platform.job");
        var rule = await s.RuleAsync("Daily", 2, ["action.raised"], Scenario.Target("email", "digest", kind: "role", reference: "member", cron: "0 8 * * *", sendEmpty: true));
        var ruleId = rule.GetProperty("id").GetGuid();
        var target = rule.GetProperty("targets")[0].GetProperty("id").GetGuid();
        var owner = await s.OwnerIdAsync();

        Task<DateTime> NextAsync() => host.ScalarAsync<DateTime>($"SELECT next_digest_at FROM notify.rule_target WHERE id = '{target}'");
        Task<IReadOnlyList<string>> DigestsAsync() => host.RowsAsync(
            $"SELECT CASE recipient_key WHEN 'user:{owner}' THEN 'owner' ELSE recipient_key END || ' ' || to_char(period_end AT TIME ZONE 'UTC', 'MM-DD HH24:MI') FROM notify.delivery WHERE kind = 'digest' ORDER BY period_end");
        void At(DateTime utc) => host.Time.SetUtcNow(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));

        var first = await NextAsync();
        At(first);
        await host.RunAsync(new SendDigest(target), s.Organization);
        await host.RunAsync(new SendDigest(target), s.Organization);
        Assert.Equal([$"owner {first:MM-dd HH:mm}"], await DigestsAsync());

        var second = await NextAsync();
        Assert.Equal(first.AddDays(1), second);
        At(second.AddDays(3).AddHours(2));
        await host.RunAsync(new SendDigest(target), s.Organization);
        Assert.Equal([$"owner {first:MM-dd HH:mm}", $"owner {second:MM-dd HH:mm}"], await DigestsAsync());
        Assert.Equal(second.AddDays(4), await NextAsync());
        Assert.Equal(second, await host.ScalarAsync<DateTime>($"SELECT last_digest_at FROM notify.rule_target WHERE id = '{target}'"));

        await host.ExecuteAsync($"UPDATE notify.rule_target SET next_digest_at = NULL, last_digest_at = NULL WHERE id = '{target}'");
        await host.RunAsync(new SendDigest(target), s.Organization);
        Assert.Equal(second.AddDays(4), await NextAsync());
        Assert.Equal(2, (await DigestsAsync()).Count);

        await host.ExecuteAsync($"DELETE FROM notify.rule WHERE id = '{ruleId}'");
        await host.RunAsync(new SendDigest(target), s.Organization);
        Assert.Equal(2, (await DigestsAsync()).Count);
        Assert.Empty(await host.RunJobsAsync());
    }
}
