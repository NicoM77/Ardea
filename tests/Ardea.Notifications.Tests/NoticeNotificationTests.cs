using System.Text.Json;
using Ardea.Assessment.Contracts;
using Ardea.Iam.Contracts;
using Ardea.Notifications.Messages;
using Ardea.TestSupport;

namespace Ardea.Notifications.Tests;

/// <summary>
/// The event <c>notice.raised</c> (master plan 8.2, 17.3; task 5a.6, ADR 048): every member hears about a new notice in the
/// app, the default rules bring an actively exploited one to the admins at once and every one to the weekly summary, a
/// notice is announced once per recipient and channel and its return once more, and a reverse search gives one summary.
/// </summary>
public sealed class NoticeNotificationTests(PostgresServer server)
{
    [Fact]
    public async Task An_exploited_notice_reaches_the_admins_at_once_and_a_plain_one_every_member_in_the_app_and_the_weekly_summary()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host, keepDefaultRules: true);
        using var bob = await host.RegisterAsync("bob@example.org", "Bob");
        await host.AddMemberAsync(s.Organization, "bob@example.org", "viewer");
        var (_, postgres) = await s.StackEntryAsync();

        var exploited = await host.NoticeAsync(s.Organization, [postgres], "CVE-2026-9001", exploitation: "active");
        await s.EventAsync("notice.raised", notice: exploited);
        await s.EvaluateAsync();
        await s.DeliverAsync();

        Assert.Equal(
            ["notice.raised|[Notice] CVE-2026-9001: PostgreSQL: remote code execution|/o/acme/notices", "notice.raised|[Notice] CVE-2026-9001: PostgreSQL: remote code execution|/o/acme/notices"],
            await host.RowsAsync("SELECT kind || '|' || title || '|' || link FROM notify.notification"));
        // Bob signed up: his confirmation mail is the only other one.
        var mail = Assert.Single(host.Mail!.Sent, m => m.Subject.StartsWith("[Notice]", StringComparison.Ordinal));
        Assert.Equal("owner@example.org", mail.To);
        Assert.Equal("[Notice] CVE-2026-9001: PostgreSQL: remote code execution", mail.Subject);
        Assert.Contains("Your stack names PostgreSQL.", mail.Text, StringComparison.Ordinal);
        Assert.Contains("CVSS 9.8. It is actively exploited.", mail.Text, StringComparison.Ordinal);
        Assert.Contains("https://ardea.test/o/acme/notices", mail.Text, StringComparison.Ordinal);

        // A plain notice: in the app for everyone, and in the weekly summary, not by e-mail at once.
        var plain = await host.NoticeAsync(s.Organization, [postgres], "CVE-2026-9002");
        var raised = await s.EventAsync("notice.raised", notice: plain);
        await s.EvaluateAsync();
        await s.DeliverAsync();
        Assert.Equal(4L, await s.CountAsync("SELECT count(*) FROM notify.notification"));
        Assert.Single(host.Mail.Sent, m => m.Subject.StartsWith("[Notice]", StringComparison.Ordinal));
        Assert.Equal(["weekly"], await host.RowsAsync($"""
            SELECT r.preset FROM notify.digest_item i JOIN notify.rule_target t ON t.id = i.rule_target_id JOIN notify.rule r ON r.id = t.rule_id
            WHERE i.event_id = '{raised}'
            """));
    }

    [Fact]
    public async Task A_notice_is_announced_once_per_recipient_and_channel_and_its_return_once_more()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("Notices", 2, ["notice.raised"], Scenario.Target("in_app"), Scenario.Target("email"));
        var notice = await host.NoticeAsync(s.Organization, [s.Component], "CVE-2026-9003");

        await s.EventAsync("notice.raised", notice: notice);
        await s.EvaluateAsync();
        await s.DeliverAsync();
        // Raised anew after a withdrawal: the same notice, nothing new for the people who heard of it.
        await s.EventAsync("notice.raised", notice: notice);
        await s.EvaluateAsync();
        await s.DeliverAsync();
        Assert.Single(host.Mail!.Sent);
        Assert.Contains("Platform Ops names PostgreSQL 16.2.", host.Mail.Sent[0].Text, StringComparison.Ordinal);

        // «Doesn't affect us» came back once: one message more on each channel.
        await host.ExecuteAsync($"UPDATE assess.notice SET reappeared_at = now(), reappeared_reason = 'exploited', exploitation = 'active' WHERE id = '{notice}'");
        await s.EventAsync("notice.reappeared", notice: notice, data: """{"reason": "exploited"}""");
        await s.EvaluateAsync();
        await s.DeliverAsync();
        Assert.Equal(2, host.Mail.Sent.Count);
        Assert.Contains("Back once: answered «doesn't affect us», now actively exploited.", host.Mail.Sent[1].Text, StringComparison.Ordinal);
        Assert.Equal(
            ["in_app notice.raised", "in_app notice.reappeared", "email notice.raised", "email notice.reappeared"],
            await host.RowsAsync("SELECT channel || ' ' || event FROM notify.delivery_event ORDER BY channel DESC, occurred_at"));
    }

    [Fact]
    public async Task A_notice_answered_or_withdrawn_before_its_event_is_evaluated_is_not_announced()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("Notices", 2, ["notice.raised"], Scenario.Target("in_app"));
        var notice = await host.NoticeAsync(s.Organization, [s.Component], "CVE-2026-9004");
        await s.EventAsync("notice.raised", notice: notice);
        await host.ExecuteAsync($"UPDATE assess.notice SET state = 'withdrawn' WHERE id = '{notice}'");

        await s.EvaluateAsync();

        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.notification"));
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.pending_event"));
    }

    [Fact]
    public async Task Earlier_notices_of_a_reverse_search_are_one_summary_and_at_once_only_with_exploited_ones()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host, keepDefaultRules: true);
        var (stack, postgres) = await s.StackEntryAsync();
        for (var i = 0; i < 3; i++)
        {
            await host.NoticeAsync(s.Organization, [postgres], $"CVE-2026-910{i}", backfill: true);
        }

        await s.EventAsync("notice.backfilled", project: stack, data: """{"notices": 3, "important": 0}""");
        await s.EvaluateAsync();
        await s.DeliverAsync();
        Assert.Equal(
            ["[Notices] Earlier notices: Stack|3 earlier notice(s) for Stack; answer them on the Notices page."],
            await host.RowsAsync("SELECT title || '|' || body FROM notify.notification"));
        Assert.Empty(host.Mail!.Sent);

        await s.EventAsync("notice.backfilled", project: stack, data: """{"notices": 14, "important": 2}""");
        await s.EvaluateAsync();
        await s.DeliverAsync();
        var mail = Assert.Single(host.Mail.Sent);
        Assert.Contains("14 earlier notice(s) for Stack, 2 of them actively exploited or in KEV", mail.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_webhook_names_the_notice_its_reason_and_the_notices_page()
    {
        var notice = new NoticeFacts
        {
            Id = Guid.CreateVersion7(),
            State = "open",
            EntryKey = "CVE-2026-9010",
            EntryTitle = "PostgreSQL: remote code execution",
            CvssScore = 9.8,
            Exploitation = "active",
            InKev = true,
            Components = [new NoticeComponentFacts("PostgreSQL", "17", Guid.CreateVersion7(), "Stack", InStack: true)],
        };
        var content = new MessageContent
        {
            DeliveryId = Guid.CreateVersion7(),
            Kind = "immediate",
            Organization = new OrganizationProfile(Guid.CreateVersion7(), "en", "off") { Name = "Acme", Slug = "acme" },
            Locale = "en",
            PublicUrl = new Uri("https://ardea.test/"),
            Items = [new MessageItem { EventId = Guid.CreateVersion7(), Event = "notice.raised", At = DateTimeOffset.UtcNow, Notice = notice }],
        };

        var item = JsonDocument.Parse(MessageRenderer.Webhook(content)).RootElement.GetProperty("events")[0];

        Assert.Equal("notice.raised", item.GetProperty("event").GetString());
        Assert.Equal("https://ardea.test/o/acme/notices", item.GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("level").ValueKind);
        var body = item.GetProperty("notice");
        Assert.Equal("CVE-2026-9010", body.GetProperty("entry").GetProperty("key").GetString());
        Assert.True(body.GetProperty("kev").GetBoolean());
        Assert.Equal("active", body.GetProperty("exploitation").GetString());
        Assert.True(body.GetProperty("components")[0].GetProperty("inStack").GetBoolean());
    }
}
