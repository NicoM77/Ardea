using System.Net;
using Ardea.Notifications.Delivering;
using Ardea.Notifications.Evaluation;
using Ardea.TestSupport;

namespace Ardea.Notifications.Tests;

/// <summary>
/// Warnings without identifier and digests (master plan 8.2, 8.4, 8.8; tasks 5.1, 5.2, 5.7): a warning reaches
/// every organisation with a rule for it once in the app and once in its next digest; a digest comes at the
/// time set in the organisation's time zone, holds everything since the last one and also comes empty.
/// </summary>
public sealed class DigestAndWarningTests(PostgresServer server)
{
    [Fact]
    public async Task A_warning_without_identifier_appears_in_every_organisation_once_in_app_and_once_in_the_next_digest()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var (alice, acme) = await host.OwnerAsync("alice@example.org", "Acme");
        using var _ = alice;
        using var bob = await host.RegisterAsync("bob@example.org", "Bob");
        var globex = await NotifyTestHost.OrganizationAsync(bob, "Globex");
        await host.ExecuteAsync("DELETE FROM platform.job");

        var warning = await host.ScalarAsync<Guid>($"""
            INSERT INTO intel.security_entry (kind, primary_key, title, state, published_at, changed_at, revision, material_fingerprint, remediation_fingerprint, exploitation, fix_available, cwe_ids, authority_warning, provenance)
            VALUES ('warning', 'NCSC-2026-0042', 'Phishing wave against invoice portals', 'published', '{host.Time.GetUtcNow():O}', now(), 1, 'm', 'r', 'none', false, ARRAY[]::text[], true, jsonb_build_object()) RETURNING id
            """);

        // Intel reports the new entry; the distribution may run more than once.
        await host.RunAsync(new DistributeWarning(warning), organization: null);
        await host.RunAsync(new DistributeWarning(warning), organization: null);
        Assert.Empty(await host.RunJobsAsync());
        await host.RunAsync(new DistributeWarning(warning), organization: null);
        Assert.Empty(await host.RunJobsAsync());

        Assert.Equal(
            ["Acme|[Warning] Phishing wave against invoice portals", "Globex|[Warning] Phishing wave against invoice portals"],
            await host.RowsAsync("SELECT o.name || '|' || n.title FROM notify.notification n JOIN iam.organization o ON o.id = n.organization_id ORDER BY o.name"));
        Assert.Equal(["Acme", "Globex"], await host.RowsAsync("""
            SELECT o.name FROM notify.digest_item i JOIN iam.organization o ON o.id = i.organization_id WHERE i.event = 'warning.published' ORDER BY o.name
            """));

        // The weekly summary is the next digest that takes it; the one after does not repeat it.
        await RunDigestsUntilAsync(host, await host.ScalarAsync<DateTime>($"SELECT t.next_digest_at FROM notify.rule_target t JOIN notify.rule r ON r.id = t.rule_id WHERE r.preset = 'weekly' AND t.organization_id = '{acme}'"));
        var first = host.Mail!.Sent.Where(m => m.Text.Contains("Phishing wave", StringComparison.Ordinal)).ToList();
        Assert.Equal(["alice@example.org", "bob@example.org"], first.Select(m => m.To).Order());

        await RunDigestsUntilAsync(host, host.Time.GetUtcNow().AddDays(7));
        Assert.Equal(2, host.Mail.Sent.Count(m => m.Text.Contains("Phishing wave", StringComparison.Ordinal)));
        Assert.Equal(2, host.Mail.Sent.Count(m => m.Subject.EndsWith("nothing to do", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Digest_comes_at_the_set_time_in_the_organisations_time_zone_holds_everything_since_the_last_and_comes_empty_when_the_rule_says_so()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        var update = await s.Owner.SendAsync(HttpMethod.Patch, s.Path, new { timeZone = "America/New_York" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var rule = await s.RuleAsync("Daily", 3, ["action.raised", "action.level_raised"],
            Scenario.Target("email"),
            Scenario.Target("email", "digest", cron: "30 7 * * *", sendEmpty: true));
        await s.RuleAsync("Quiet", 3, ["action.raised"], Scenario.Target("in_app", "digest", cron: "30 7 * * *"));

        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var local = TimeZoneInfo.ConvertTime(host.Time.GetUtcNow(), zone);
        var expected = new DateTimeOffset(local.Date.AddHours(7.5), zone.GetUtcOffset(local.Date.AddHours(7.5)));
        expected = expected <= host.Time.GetUtcNow() ? NextDay(expected, zone) : expected;
        var target = rule.GetProperty("targets").EnumerateArray().Single(t => t.GetProperty("mode").GetString() == "digest");
        Assert.Equal(expected.UtcDateTime, target.GetProperty("nextDigestAt").GetDateTimeOffset().UtcDateTime);

        var first = await s.ActionAsync(2, level: 4);
        var second = await s.ActionAsync(3, level: 3);
        await s.EventAsync("action.raised", first);
        await s.EvaluateAsync();
        await s.DeliverAsync();
        Assert.Single(host.Mail!.Sent);

        // At 07:30 in New York: one digest with what happened since the rule was made; the immediate e-mail is marked.
        await RunDigestsUntilAsync(host, expected);
        var digest = host.Mail.Sent[^1];
        Assert.Equal("Ardea digest for Acme: 1 item(s)", digest.Subject);
        Assert.Contains("#2 PostgreSQL in Platform Ops", digest.Text, StringComparison.Ordinal);
        Assert.Contains("(already reported)", digest.Text, StringComparison.Ordinal);
        Assert.Equal(expected.UtcDateTime, (await host.ScalarAsync<DateTime>("SELECT period_end FROM notify.delivery WHERE kind = 'digest'")).ToUniversalTime());

        // The next day holds only what came after the first digest.
        await s.EventAsync("action.raised", second);
        await s.EvaluateAsync();
        await RunDigestsUntilAsync(host, NextDay(expected, zone));
        var next = host.Mail.Sent[^1];
        Assert.Contains("#3 PostgreSQL", next.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("#2 PostgreSQL", next.Text, StringComparison.Ordinal);

        // A day without news: the rule that says so sends an empty digest, the other sends nothing.
        var before = await s.CountAsync("SELECT count(*) FROM notify.delivery WHERE kind = 'digest'");
        await RunDigestsUntilAsync(host, NextDay(NextDay(expected, zone), zone));
        Assert.Equal("Ardea digest for Acme: nothing to do", host.Mail.Sent[^1].Subject);
        Assert.Contains("Checked: nothing needed your attention in this period.", host.Mail.Sent[^1].Text, StringComparison.Ordinal);
        Assert.Equal(before + 1, await s.CountAsync("SELECT count(*) FROM notify.delivery WHERE kind = 'digest'"));
        Assert.Equal(["in_app 2", "email 3"], await host.RowsAsync("SELECT channel || ' ' || count(*) FROM notify.delivery WHERE kind = 'digest' AND state = 'sent' GROUP BY channel ORDER BY channel DESC"));
    }

    /// <summary>Moves the clock to <paramref name="at"/> and lets the instance's schedule find and send what is due.</summary>
    private static Task RunDigestsUntilAsync(NotifyTestHost host, DateTime at) => RunDigestsUntilAsync(host, new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)));

    private static async Task RunDigestsUntilAsync(NotifyTestHost host, DateTimeOffset at)
    {
        if (at > host.Time.GetUtcNow())
        {
            host.Time.SetUtcNow(at.ToUniversalTime());
        }

        await host.RunAsync(new FindDueDigests(), organization: null);
        Assert.Empty(await host.RunJobsAsync());
    }

    private static DateTimeOffset NextDay(DateTimeOffset at, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(at, zone).DateTime.AddDays(1);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
