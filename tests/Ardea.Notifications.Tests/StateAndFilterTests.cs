using System.Net;
using System.Text.Json;
using Ardea.Notifications.Delivering;
using Ardea.TestSupport;

namespace Ardea.Notifications.Tests;

public sealed class StateAndFilterTests(PostgresServer server)
{
    [Fact]
    public async Task The_all_clear_reaches_a_channel_only_where_the_action_was_reported_the_state_sinks_within_one_batch_and_exploitation_and_fix_are_told_once_each()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        var mia = await MemberAsync(host, s);
        await s.RuleAsync("Watch", 4, ["action.raised", "action.level_raised", "action.exploitation_escalated", "action.fix_available", "action.level_lowered"],
            Scenario.Target("in_app"), Scenario.Target("email"));
        await s.RuleAsync("All-clear", 4, ["action.level_lowered"], Scenario.Target("email", kind: "user", reference: mia.ToString()));
        var action = await s.ActionAsync(2, level: 4);
        await host.ExecuteAsync($"UPDATE assess.remediation SET fix_available = false WHERE id = '{action}'");

        async Task EventAsync(string kind, string data = "{}")
        {
            await s.EventAsync(kind, action, data);
            await s.EvaluateAsync();
        }

        await EventAsync("action.raised");
        await s.SetActionAsync(action, 4, "poc");
        await EventAsync("action.exploitation_escalated");
        await EventAsync("action.exploitation_escalated");
        await s.SetActionAsync(action, 4, "active");
        await EventAsync("action.exploitation_escalated");
        await host.ExecuteAsync($"UPDATE assess.remediation SET fix_available = true WHERE id = '{action}'");
        await EventAsync("action.fix_available");
        await EventAsync("action.fix_available");
        await s.SetActionAsync(action, 3, "active");
        await EventAsync("action.level_lowered", """{"from": 4, "to": 3}""");

        string[] told = ["action.exploitation_escalated 2", "action.fix_available 1", "action.level_lowered 1", "action.raised 1"];
        Assert.Equal(told, await host.RowsAsync($"SELECT kind || ' ' || count(*) FROM notify.notification WHERE remediation_id = '{action}' GROUP BY kind ORDER BY kind"));
        var owner = await s.OwnerIdAsync();
        Assert.Equal(told, await host.RowsAsync($"SELECT event || ' ' || count(*) FROM notify.delivery_event WHERE channel = 'email' AND recipient_key = 'user:{owner}' AND remediation_id = '{action}' GROUP BY event ORDER BY event"));

        var again = await s.ActionAsync(3, level: 5);
        await s.EventAsync("action.raised", again, """{"level": 5}""");
        await s.EventAsync("action.level_lowered", again, """{"from": 5, "to": 4}""");
        await s.EventAsync("action.level_raised", again, """{"from": 4, "to": 5}""");
        await s.EvaluateAsync();

        string[] sameBatch = ["action.level_lowered", "action.level_raised", "action.raised"];
        Assert.Equal(sameBatch, await host.RowsAsync($"SELECT kind FROM notify.notification WHERE remediation_id = '{again}' ORDER BY kind"));
        Assert.Equal(sameBatch, await host.RowsAsync($"SELECT event FROM notify.delivery_event WHERE channel = 'email' AND recipient_key = 'user:{owner}' AND remediation_id = '{again}' ORDER BY event"));
        Assert.Equal(0L, await s.CountAsync($"SELECT count(*) FROM notify.delivery_event WHERE recipient_key = 'user:{mia}'"));
    }

    [Fact]
    public async Task A_rule_for_one_project_and_level_hears_neither_other_projects_nor_lower_levels_at_once_nor_in_the_growth_of_its_digest()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        var (shop, shopComponent) = await ShopAsync(host, s);
        await RuleAsync(s, "Ops", 4, [s.Project], Scenario.Target("in_app"), Scenario.Target("email", "digest", cron: "0 8 * * *"));
        var other = await host.ActionAsync(s.Organization, shop, shopComponent, 10, level: 5);
        var low = await s.ActionAsync(3, level: 3);
        var ops = await s.ActionAsync(2, level: 4);

        foreach (var action in new[] { other, low, ops })
        {
            await s.EventAsync("action.raised", action);
        }

        await s.EvaluateAsync();
        Assert.Equal(["[4 High] #2 PostgreSQL in Platform Ops"], await host.RowsAsync("SELECT title FROM notify.notification"));
        Assert.Equal([ops.ToString()], await host.RowsAsync("SELECT remediation_id FROM notify.digest_item"));
        await DigestAsync(host);
        Assert.Contains("#2 PostgreSQL in Platform Ops", Assert.Single(host.Mail!.Sent).Text, StringComparison.Ordinal);

        await GrowAsync(host, s, host.Time.GetUtcNow().AddHours(1), other, low);
        await DigestAsync(host);
        Assert.Single(host.Mail.Sent);

        await GrowAsync(host, s, host.Time.GetUtcNow().AddHours(1), ops);
        await DigestAsync(host);
        Assert.Equal(2, host.Mail.Sent.Count);
        Assert.Contains("#2 PostgreSQL in Platform Ops", host.Mail.Sent[^1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Shop", host.Mail.Sent[^1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("#3 ", host.Mail.Sent[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_reminders_by_level_follow_the_rules_project_and_level_and_a_muted_channel_hears_only_level_5()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        var mia = await MemberAsync(host, s, mutedEmail: true);
        var (shop, shopComponent) = await ShopAsync(host, s);
        await RuleAsync(s, "Ops", 4, [s.Project], Scenario.Target("email", kind: "role", reference: "member"));
        var critical = await s.ActionAsync(2, level: 5);
        var high = await s.ActionAsync(3, level: 4);
        await s.ActionAsync(4, level: 3);
        var other = await host.ActionAsync(s.Organization, shop, shopComponent, 10, level: 5);
        await host.ExecuteAsync($"""
            UPDATE assess.remediation SET opened_at = '{host.Time.GetUtcNow().AddDays(-40):O}';
            INSERT INTO notify.reminder_schedule (organization_id, level5, level4, level3, next_run_at, changed_at)
            VALUES ('{s.Organization}', 'daily', 'weekly', 'monthly', '{host.Time.GetUtcNow().AddMinutes(-1):O}', now());
            DELETE FROM platform.job;
            """);

        await host.RunAsync(new SendReminders(), s.Organization);
        await host.RunAsync(new SendReminders(), s.Organization);
        Assert.Empty(await host.RunJobsAsync());

        var owner = await s.OwnerIdAsync();
        Assert.Equal(
            [$"member {critical}", $"owner {critical},{high}"],
            await host.RowsAsync($"""
                SELECT CASE recipient_key WHEN 'user:{owner}' THEN 'owner' WHEN 'user:{mia}' THEN 'member' END || ' ' || string_agg(remediation_id::text, ',' ORDER BY level DESC)
                FROM notify.delivery_event WHERE event = 'reminder.due' GROUP BY recipient_key ORDER BY 1
                """));
        Assert.Equal(2L, await s.CountAsync("SELECT count(*) FROM notify.delivery WHERE kind = 'reminder' AND state = 'sent'"));
        Assert.Equal(
            new[] { critical, high }.Select(id => id.ToString()).Order(),
            (await host.RowsAsync("SELECT remediation_id FROM notify.reminder_state")).Order());
        Assert.DoesNotContain(other.ToString(), await host.RowsAsync("SELECT remediation_id FROM notify.delivery_event"));
    }

    private static async Task<Guid> MemberAsync(NotifyTestHost host, Scenario s, bool mutedEmail = false)
    {
        using var member = await host.RegisterAsync("member@example.org", "Mia Member");
        await host.AddMemberAsync(s.Organization, "member@example.org", "member");
        if (mutedEmail)
        {
            Assert.Equal(HttpStatusCode.OK, (await member.PutAsync($"{s.Path}/notification-preferences", new { mutedChannels = new[] { "email" } })).StatusCode);
        }

        await host.ExecuteAsync("DELETE FROM platform.job WHERE kind LIKE 'iam.%'");
        return await host.ScalarAsync<Guid>("SELECT id FROM iam.\"user\" WHERE normalized_email = 'MEMBER@EXAMPLE.ORG'");
    }

    private static async Task<(Guid Project, Guid Component)> ShopAsync(NotifyTestHost host, Scenario s)
    {
        var shop = await host.ProjectAsync(s.Owner, s.Organization, "Shop", "postgresql", "15.1");
        await host.ExecuteAsync("DELETE FROM platform.job WHERE kind LIKE 'assets.%' OR kind LIKE 'assess.%'");
        return shop;
    }

    private static async Task<JsonElement> RuleAsync(Scenario s, string name, int minLevel, Guid[] projectIds, params object[] targets) =>
        await NotifyTestHost.JsonAsync(
            await s.Owner.PostAsync($"{s.Path}/rules", new { name, minLevel, events = new[] { "action.raised" }, projectIds, targets }), HttpStatusCode.Created);

    private static Task GrowAsync(NotifyTestHost host, Scenario s, DateTimeOffset at, params Guid[] actions) => host.ExecuteAsync(string.Concat(actions.Select(action => $$"""
        INSERT INTO assess.assess_event (id, organization_id, remediation_id, at, kind, data)
        VALUES (uuidv7(), '{{s.Organization}}', '{{action}}', '{{at:O}}', 'finding.joined', '{"findings": 1}');
        """)));

    private static async Task DigestAsync(NotifyTestHost host)
    {
        var due = await host.ScalarAsync<DateTime>("SELECT min(next_digest_at) FROM notify.rule_target WHERE mode = 'digest'");
        var dueAt = new DateTimeOffset(DateTime.SpecifyKind(due, DateTimeKind.Utc));
        if (dueAt > host.Time.GetUtcNow())
        {
            host.Time.SetUtcNow(dueAt);
        }
        await host.RunAsync(new FindDueDigests(), organization: null);
        Assert.Empty(await host.RunJobsAsync());
    }
}
