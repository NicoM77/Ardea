using System.Net;
using Ardea.Notifications.Delivering;
using Ardea.TestSupport;
using MailKit.Net.Smtp;

namespace Ardea.Notifications.Tests;

public sealed class DeliveryEdgeTests(PostgresServer server)
{
    [Fact]
    public async Task A_recipient_who_left_unsubscribed_lost_the_device_or_was_switched_off_before_the_message_went_out_gets_nothing_nor_does_anyone_about_a_withdrawn_notice()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        using var member = await host.RegisterAsync("member@example.org", "Mia Member");
        await host.AddMemberAsync(s.Organization, "member@example.org", "member");
        var mia = await host.ScalarAsync<Guid>("SELECT id FROM iam.\"user\" WHERE normalized_email = 'MEMBER@EXAMPLE.ORG'");
        var address = await host.ScalarAsync<Guid>($"""
            INSERT INTO notify.external_address (organization_id, address, normalized_address, created_at, confirmed_at)
            VALUES ('{s.Organization}', 'oncall@partner.example', 'ONCALL@PARTNER.EXAMPLE', now(), now()) RETURNING id
            """);
        var endpoint = (await NotifyTestHost.JsonAsync(await s.Owner.PostAsync($"{s.Path}/endpoints", new { name = "SIEM", url = "https://203.0.113.7/hook" }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var owner = await s.OwnerIdAsync();
        await host.ExecuteAsync($"""
            INSERT INTO notify.push_subscription (user_id, endpoint, endpoint_hash, p256dh, auth, label, created_at, failed_count, rejected_count)
            VALUES ('{owner}', 'https://fcm.googleapis.com/fcm/send/phone', 'phone', 'key', 'auth', 'Phone', now(), 0, 0)
            """);
        await s.RuleAsync("Everyone", 2, ["action.raised", "notice.raised"],
            Scenario.Target("email"),
            Scenario.Target("push"),
            Scenario.Target("email", kind: "user", reference: mia.ToString()),
            Scenario.Target("email", kind: "address", reference: address.ToString()),
            Scenario.Target("webhook", kind: "endpoint", reference: endpoint.ToString()));
        await host.ExecuteAsync("DELETE FROM platform.job WHERE kind LIKE 'iam.%'");
        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 4));
        await s.EvaluateAsync();
        Assert.Equal(5L, await s.CountAsync("SELECT count(*) FROM notify.delivery WHERE state = 'pending'"));

        await host.ExecuteAsync($"DELETE FROM iam.membership WHERE user_id = '{mia}'");
        await host.ExecuteAsync($"UPDATE notify.external_address SET unsubscribed_at = now() WHERE id = '{address}'");
        await host.ExecuteAsync("DELETE FROM notify.push_subscription");
        Assert.Equal(HttpStatusCode.OK, (await s.Owner.PutAsync($"{s.Path}/endpoints/{endpoint}", new { enabled = false })).StatusCode);
        await s.DeliverAsync();

        Assert.Equal(
            ["address email suppressed", "endpoint webhook suppressed", "member email suppressed", "owner email sent", "owner push suppressed"],
            await host.RowsAsync($"SELECT CASE recipient_key WHEN 'user:{owner}' THEN 'owner' WHEN 'user:{mia}' THEN 'member' ELSE split_part(recipient_key, ':', 1) END || ' ' || channel || ' ' || state FROM notify.delivery ORDER BY 1"));
        Assert.Equal(
            ["The member has no device subscribed to push any more.", "The recipient is no longer reachable: left the organisation, unsubscribed, or the endpoint is off."],
            await host.RowsAsync("SELECT DISTINCT last_error FROM notify.delivery WHERE state = 'suppressed' ORDER BY 1"));
        Assert.Equal(["owner@example.org"], host.Mail!.Sent.Where(m => m.Subject.StartsWith("[High]", StringComparison.Ordinal)).Select(m => m.To));

        var notice = await host.NoticeAsync(s.Organization, [s.Component], "CVE-2026-9101");
        await s.EventAsync("notice.raised", notice: notice);
        await s.EvaluateAsync();
        await host.ExecuteAsync($"UPDATE assess.notice SET state = 'withdrawn' WHERE id = '{notice}'");
        await s.DeliverAsync();
        Assert.Equal(
            ["suppressed|None of its events exists any more."],
            await host.RowsAsync("SELECT d.state || '|' || d.last_error FROM notify.delivery d JOIN notify.delivery_event e ON e.delivery_id = d.id WHERE e.event = 'notice.raised' AND d.channel = 'email'"));
        Assert.DoesNotContain(host.Mail.Sent, m => m.Subject.Contains("CVE-2026-9101", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_smtp_e_mail_is_off_from_the_rule_to_the_digest_and_a_channel_that_fails_before_the_call_tries_again()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        var mail = host.Mail!;
        mail.Configured = () => false;

        var channels = await NotifyTestHost.JsonAsync(await s.Owner.GetAsync($"{s.Path}/notification-channels"));
        var email = channels.EnumerateArray().Single(c => c.GetProperty("channel").GetString() == "email");
        Assert.Equal((false, "smtp_not_configured"), (email.GetProperty("available").GetBoolean(), email.GetProperty("reason").GetString()));
        var rule = await s.RuleAsync("Mail", 2, ["action.raised"],
            Scenario.Target("in_app"), Scenario.Target("email"), Scenario.Target("email", "digest", cron: "0 8 * * *", sendEmpty: true));
        Assert.Equal(["email_unavailable"], rule.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));
        var refused = await s.Owner.PostAsync($"{s.Path}/external-addresses", new { address = "oncall@partner.example" });
        Assert.Equal("smtp_not_configured", (await NotifyTestHost.JsonAsync(refused, HttpStatusCode.Conflict)).GetProperty("code").GetString());
        var second = await s.ActionAsync(3, level: 4);
        var third = await s.ActionAsync(4, level: 4);

        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 4));
        await s.EvaluateAsync();
        Assert.Equal(["in_app"], await host.RowsAsync("SELECT channel FROM notify.delivery"));
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.digest_item"));

        mail.Configured = () => true;
        await s.EventAsync("action.raised", second);
        await s.EvaluateAsync();
        mail.Configured = () => false;
        await s.DeliverAsync();
        Assert.Equal("suppressed|SMTP is not configured.", await host.ScalarAsync<string>("SELECT state || '|' || last_error FROM notify.delivery WHERE channel = 'email'"));

        mail.Configured = () => true;
        await s.EventAsync("action.raised", third);
        await s.EvaluateAsync();
        mail.Configured = () => throw new InvalidOperationException("The settings cannot be read.");
        host.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.Single(await host.RunJobsAsync());
        Assert.Equal("failed|InvalidOperationException: the channel failed.", await host.ScalarAsync<string>("SELECT state || '|' || last_error FROM notify.delivery WHERE channel = 'email' AND state <> 'suppressed'"));
        mail.Configured = () => true;
        host.Time.Advance(TimeSpan.FromSeconds(30));
        Assert.Empty(await host.RunJobsAsync());
        Assert.Equal("sent|2", await host.ScalarAsync<string>("SELECT state || '|' || attempts FROM notify.delivery WHERE channel = 'email' AND state <> 'suppressed'"));
        Assert.Single(mail.Sent);

        mail.Configured = () => false;
        var target = rule.GetProperty("targets").EnumerateArray().Single(t => t.GetProperty("mode").GetString() == "digest").GetProperty("id").GetGuid();
        var due = await host.ScalarAsync<DateTime>($"SELECT next_digest_at FROM notify.rule_target WHERE id = '{target}'");
        var dueAt = new DateTimeOffset(DateTime.SpecifyKind(due, DateTimeKind.Utc));
        if (dueAt > host.Time.GetUtcNow())
        {
            host.Time.SetUtcNow(dueAt);
        }
        await host.RunAsync(new FindDueDigests(), organization: null);
        Assert.Empty(await host.RunJobsAsync());
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.delivery WHERE kind = 'digest'"));
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.digest_item WHERE period_end IS NULL"));
        Assert.True(await host.ScalarAsync<DateTime>($"SELECT next_digest_at FROM notify.rule_target WHERE id = '{target}'") > due);
    }

    [Fact]
    public async Task A_lost_level_5_warns_the_owners_only_when_it_reached_its_recipient_nowhere_and_a_lost_digest_never()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("Critical", 5, ["action.raised"], Scenario.Target("in_app"), Scenario.Target("email"));
        var digest = await s.RuleAsync("Daily", 2, ["action.raised"], Scenario.Target("email", "digest", cron: "0 8 * * *"));
        host.Mail!.Failure = () => new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "5.1.1 User unknown");

        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 5));
        await s.EvaluateAsync();
        await s.DeliverAsync();

        Assert.Equal("dead|1", await host.ScalarAsync<string>("SELECT state || '|' || attempts FROM notify.delivery WHERE channel = 'email'"));
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.notification WHERE kind = 'action.raised'"));
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.notification WHERE kind = 'delivery.failed'"));

        var target = digest.GetProperty("targets")[0].GetProperty("id").GetGuid();
        var due = await host.ScalarAsync<DateTime>($"SELECT next_digest_at FROM notify.rule_target WHERE id = '{target}'");
        var dueAt = new DateTimeOffset(DateTime.SpecifyKind(due, DateTimeKind.Utc));
        if (dueAt > host.Time.GetUtcNow())
        {
            host.Time.SetUtcNow(dueAt);
        }
        await host.RunAsync(new FindDueDigests(), organization: null);
        Assert.Empty(await host.RunJobsAsync());
        Assert.Equal("dead", await host.ScalarAsync<string>("SELECT state FROM notify.delivery WHERE kind = 'digest'"));
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.notification WHERE kind = 'delivery.failed'"));
    }

    [Fact]
    public async Task The_hourly_cap_counts_the_immediate_messages_of_the_channel_within_the_last_hour_exactly()
    {
        await using var host = await NotifyTestHost.StartAsync(server, start: DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("High", 4, ["action.raised"], Scenario.Target("email"));
        var actions = new[] { await s.ActionAsync(2, level: 4), await s.ActionAsync(3, level: 4), await s.ActionAsync(4, level: 4) };
        var start = host.Time.GetUtcNow();
        await host.ExecuteAsync($"""
            INSERT INTO notify.delivery (id, organization_id, kind, channel, recipient_key, send_after, state, attempts, created_at)
            SELECT gen_random_uuid(), '{s.Organization}', 'immediate', channel, 'user:' || gen_random_uuid(), at, state, 1, at
            FROM (VALUES
                ('email', 'sent', '{start.AddMinutes(-30):O}'::timestamptz, {Evaluation.Evaluator.HourlyLimit - 1}),
                ('email', 'sent', '{start.AddHours(-1):O}'::timestamptz, 1),
                ('email', 'suppressed', '{start.AddMinutes(-10):O}'::timestamptz, 5),
                ('webhook', 'sent', '{start.AddMinutes(-10):O}'::timestamptz, 10)) AS v(channel, state, at, n),
            generate_series(1, n)
            """);
        var owner = await s.OwnerIdAsync();

        async Task<string> RaiseAsync(Guid action)
        {
            await s.EventAsync("action.raised", action);
            await s.EvaluateAsync();
            return await host.ScalarAsync<string>($"SELECT state FROM notify.delivery WHERE recipient_key = 'user:{owner}' ORDER BY created_at DESC LIMIT 1");
        }

        Assert.Equal("pending", await RaiseAsync(actions[0]));
        await s.DeliverAsync();
        Assert.Equal("suppressed", await RaiseAsync(actions[1]));
        Assert.Equal(
            $"More than {Evaluation.Evaluator.HourlyLimit} immediate messages on this channel within an hour; the event waits for the digest.",
            await host.ScalarAsync<string>($"SELECT last_error FROM notify.delivery WHERE state = 'suppressed' AND recipient_key = 'user:{owner}'"));

        host.Time.Advance(TimeSpan.FromMinutes(28));
        Assert.Equal("pending", await RaiseAsync(actions[2]));
    }
}
