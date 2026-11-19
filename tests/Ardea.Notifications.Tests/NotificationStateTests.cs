using Ardea.Notifications.Delivering;
using Ardea.TestSupport;

namespace Ardea.Notifications.Tests;

/// <summary>
/// The acceptance criteria of master plan 8.8 on state and uniqueness (tasks 5.1, 5.2): never the same twice,
/// one message per recipient and channel whichever rules fire, again only on a rise, bundling, and one
/// message for a first fill.
/// </summary>
public sealed class NotificationStateTests(PostgresServer server)
{
    private static readonly string[] Raises = ["action.raised", "action.level_raised", "action.exploitation_escalated", "backfill.completed"];

    [Fact]
    public async Task Above_sixty_immediate_messages_an_hour_on_a_channel_the_next_waits_for_the_digest()
    {
        // A flood from a faulty or hostile source (master plan 9.7) ends at the cap of 8.6 per channel and organisation.
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("High", 4, ["action.raised"], Scenario.Target("email"));
        var earlier = host.Time.GetUtcNow().AddMinutes(-30);
        await host.ExecuteAsync($"""
            INSERT INTO notify.delivery (id, organization_id, kind, channel, recipient_key, send_after, state, attempts, created_at)
            SELECT gen_random_uuid(), '{s.Organization}', 'immediate', 'email', 'user:' || gen_random_uuid(), '{earlier:O}', 'sent', 1, '{earlier:O}'
            FROM generate_series(1, {Evaluation.Evaluator.HourlyLimit})
            """);

        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 4));
        await s.EvaluateAsync();

        Assert.Equal("suppressed", await host.ScalarAsync<string>($"SELECT state FROM notify.delivery WHERE created_at > '{earlier:O}'"));
    }

    [Fact]
    public async Task Same_event_processed_three_times_yields_one_delivery_per_recipient_and_channel()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("Everything", 2, Raises, Scenario.Target("in_app"), Scenario.Target("email"));
        var action = await s.ActionAsync(2, level: 4);

        var raised = await s.EventAsync("action.raised", action);
        await s.EvaluateAsync();
        await host.RequeueAsync(s.Organization, raised, "action.raised");
        await s.EvaluateAsync();
        await host.RequeueAsync(s.Organization, raised, "action.raised");
        await s.EvaluateAsync();

        Assert.Equal(
            ["in_app 1", "email 1"],
            await host.RowsAsync($"SELECT channel || ' ' || count(*) FROM notify.delivery_event WHERE event_id = '{raised}' GROUP BY channel ORDER BY channel DESC"));
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.notification"));

        // The delivery job running twice sends once.
        await s.DeliverAsync();
        var delivery = await host.ScalarAsync<Guid>("SELECT id FROM notify.delivery WHERE channel = 'email'");
        await host.RunAsync(new Deliver(delivery), s.Organization);
        Assert.Single(host.Mail!.Sent);
        Assert.StartsWith("[4 High] #2 PostgreSQL in Platform Ops", host.Mail.Sent[0].Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_rules_with_the_same_recipient_and_channel_yield_one_message_for_an_event()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("Owners", 3, Raises, Scenario.Target("in_app", reference: "owner"), Scenario.Target("email", reference: "owner"));
        await s.RuleAsync("Admins and up", 2, Raises, Scenario.Target("in_app", reference: "admin"), Scenario.Target("email", reference: "admin"));
        var action = await s.ActionAsync(2, level: 5);

        await s.EventAsync("action.raised", action);
        await s.EvaluateAsync();
        await s.DeliverAsync();

        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.notification"));
        Assert.Equal(2L, await s.CountAsync("SELECT count(*) FROM notify.delivery"));
        Assert.Single(host.Mail!.Sent);
    }

    [Fact]
    public async Task A_raised_level_notifies_again_but_more_findings_or_a_new_description_do_not()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("High and up", 4, Raises, Scenario.Target("in_app"));
        var action = await s.ActionAsync(2, level: 4);

        await s.EventAsync("action.raised", action);
        await s.EvaluateAsync();
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.notification"));

        // More findings without a rise are history only; a new description changes nothing in Assessment, so no event comes.
        await host.ExecuteAsync($"UPDATE assess.remediation SET open_findings = 5 WHERE id = '{action}'");
        await s.EventAsync("finding.joined", action, """{"level": 4}""");
        await s.EvaluateAsync();
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.notification"));

        await s.SetActionAsync(action, 5);
        await s.EventAsync("action.level_raised", action, """{"from": 4, "to": 5}""");
        await s.EvaluateAsync();
        Assert.Equal(
            ["action.level_raised|[5 Critical] #2 PostgreSQL in Platform Ops", "action.raised|[4 High] #2 PostgreSQL in Platform Ops"],
            await host.RowsAsync("SELECT kind || '|' || title FROM notify.notification ORDER BY created_at DESC, kind"));
    }

    [Fact]
    public async Task Raise_lower_and_raise_again_yield_two_raise_messages_also_for_a_recipient_without_the_all_clear()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        // E-mail at once for rises only: this recipient does not hear the all-clear in between.
        await s.RuleAsync("Critical", 5, Raises, Scenario.Target("email"));
        var action = await s.ActionAsync(2, level: 4);

        await s.SetActionAsync(action, 5);
        await s.EventAsync("action.level_raised", action, """{"from": 4, "to": 5}""");
        await s.EvaluateAsync();
        await s.DeliverAsync();

        await s.SetActionAsync(action, 4);
        await s.EventAsync("action.level_lowered", action, """{"from": 5, "to": 4}""");
        await s.EvaluateAsync();
        Assert.Equal(4, await host.ScalarAsync<int>($"SELECT last_level FROM notify.recipient_state WHERE remediation_id = '{action}'"));

        await s.SetActionAsync(action, 5);
        await s.EventAsync("action.level_raised", action, """{"from": 4, "to": 5}""");
        await s.EvaluateAsync();
        await s.DeliverAsync();

        Assert.Equal(2, host.Mail!.Sent.Count);
        Assert.All(host.Mail.Sent, m => Assert.Contains("Raised from level 4.", m.Text, StringComparison.Ordinal));
        Assert.Equal(["action.level_raised", "action.level_raised"], await host.RowsAsync("SELECT event FROM notify.delivery_event ORDER BY occurred_at"));
    }

    [Fact]
    public async Task Three_events_for_the_same_recipient_within_two_minutes_yield_one_message()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("All raises", 2, Raises, Scenario.Target("email"));
        var first = await s.ActionAsync(2, level: 3);
        var second = await s.ActionAsync(3, level: 4);
        var third = await s.ActionAsync(4, level: 5, exploitation: "active");

        await s.EventAsync("action.raised", first);
        await s.EvaluateAsync();
        host.Time.Advance(TimeSpan.FromSeconds(40));
        await s.EventAsync("action.raised", second);
        await s.EvaluateAsync();
        host.Time.Advance(TimeSpan.FromSeconds(40));
        await s.EventAsync("action.raised", third);
        await s.EvaluateAsync();
        await s.DeliverAsync();

        var mail = Assert.Single(host.Mail!.Sent);
        Assert.StartsWith("[5 Critical] 3 updates in Acme", mail.Subject, StringComparison.Ordinal);
        Assert.Contains("#2 PostgreSQL", mail.Text, StringComparison.Ordinal);
        Assert.Contains("It is actively exploited.", mail.Text, StringComparison.Ordinal);
        Assert.Equal(["3"], await host.RowsAsync("SELECT count(*) FROM notify.delivery_event GROUP BY delivery_id"));

        // After the message went out, the next event starts a new one.
        var fourth = await s.ActionAsync(5, level: 3);
        await s.EventAsync("action.raised", fourth);
        await s.EvaluateAsync();
        await s.DeliverAsync();
        Assert.Equal(2, host.Mail.Sent.Count);
    }

    [Fact]
    public async Task A_new_project_with_forty_hits_in_the_look_back_yields_exactly_one_message()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("All raises", 2, Raises, Scenario.Target("in_app"), Scenario.Target("email"));
        var actions = new List<Guid>();
        for (var i = 0; i < 40; i++)
        {
            actions.Add(await s.ActionAsync(10 + i, level: i % 10 == 0 ? 4 : 3, backfill: true));
        }

        // The first fill writes no action.raised, only one backfill.completed for the project (ADR 035; Ardea.Assessment.Tests.ActionTests).
        await s.EventAsync("backfill.completed", project: s.Project, data: """{"scope": "project", "actions": 40, "highestLevel": 4, "findings": 40}""");
        await s.EvaluateAsync();
        await s.DeliverAsync();

        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.notification"));
        var mail = Assert.Single(host.Mail!.Sent);
        Assert.Equal("First assessment: Platform Ops", mail.Subject);
        Assert.Contains("40 action(s), highest level 4", mail.Text, StringComparison.Ordinal);
        // The actions count as reported to everyone who heard about the first fill: only a later rise sends.
        Assert.Equal(80L, await s.CountAsync("SELECT count(*) FROM notify.recipient_state"));

        await s.SetActionAsync(actions[1], 4);
        await s.EventAsync("action.level_raised", actions[1], """{"from": 3, "to": 4}""");
        await s.EvaluateAsync();
        Assert.Equal(2L, await s.CountAsync("SELECT count(*) FROM notify.notification"));
    }
}
