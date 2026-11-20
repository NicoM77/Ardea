using System.Net;
using Ardea.Notifications.Delivering;
using Ardea.Notifications.Rules;
using Ardea.TestSupport;

namespace Ardea.Notifications.Tests;

/// <summary>
/// Reminders by level (master plan 8.7, 17.5; task 5a.6, ADR 048): open reminders remind their recipients daily at level 5,
/// weekly at 4, monthly at 3 by default, on weekdays at 08:00 in the organisation's time zone, in one message per recipient
/// and channel; done reminders and levels below 3 never; the rhythm is set per organisation. The clock is the test's.
/// </summary>
public sealed class ReminderTests(PostgresServer server)
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("daily", 30, true)]
    [InlineData("daily", 13, true)]
    [InlineData("daily", 11, false)]
    [InlineData("weekly", 6 * 24, false)]
    [InlineData("weekly", 7 * 24 - 12, true)]
    [InlineData("monthly", 29 * 24, false)]
    [InlineData("monthly", 31 * 24, true)]
    [InlineData("off", 400 * 24, false)]
    public void A_reminder_is_due_one_rhythm_after_it_reached_its_level_less_half_a_day(string rhythm, int hoursSince, bool due) =>
        Assert.Equal(due, ReminderRhythms.IsDue(rhythm, Monday, null, Monday.AddHours(hoursSince)));

    [Fact]
    public void The_rhythm_counts_from_the_last_reminder_when_that_is_later()
    {
        var reminded = Monday.AddDays(10);
        Assert.False(ReminderRhythms.IsDue(ReminderRhythms.Weekly, Monday, reminded, reminded.AddDays(3)));
        Assert.True(ReminderRhythms.IsDue(ReminderRhythms.Weekly, Monday, reminded, reminded.AddDays(7)));
    }

    [Fact]
    public async Task Open_reminders_remind_by_level_in_one_message_per_recipient_on_weekdays_in_the_organisations_time_zone()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host, keepDefaultRules: true);
        Assert.Equal(HttpStatusCode.OK, (await s.Owner.SendAsync(HttpMethod.Patch, s.Path, new { timeZone = "Europe/Zurich" })).StatusCode);
        var critical = await s.ActionAsync(2, level: 5);
        var high = await s.ActionAsync(3, level: 4);
        await s.ActionAsync(4, level: 3);
        var low = await s.ActionAsync(5, level: 2);
        var done = await s.ActionAsync(6, level: 5);
        await host.ExecuteAsync($"""
            UPDATE assess.remediation SET opened_at = '{host.Time.GetUtcNow().AddDays(-2):O}';
            UPDATE assess.remediation SET opened_at = '{host.Time.GetUtcNow().AddDays(-8):O}' WHERE id IN ('{high}', '{low}');
            UPDATE assess.remediation SET status = 'closed', closed_at = now(), close_reason = 'remediated' WHERE id = '{done}';
            """);
        await host.ExecuteAsync("DELETE FROM platform.job");

        // The first sweep writes the defaults and schedules the first run: the next weekday at 08:00 in Zurich.
        await host.RunAsync(new FindDueDigests(), organization: null);
        Assert.Empty(await host.RunJobsAsync());
        var first = await NextRunAsync(host);
        Assert.Equal(DigestSchedule.Next(ReminderRhythms.Schedule, "Europe/Zurich", host.Time.GetUtcNow()), first);
        Assert.Equal(["daily weekly monthly"], await host.RowsAsync("SELECT level5 || ' ' || level4 || ' ' || level3 FROM notify.reminder_schedule"));
        Assert.Empty(Reminders(host));

        // The run: level 5 (daily) and 4 (weekly) are due; level 3 is not a month old, level 2 and the done one never remind.
        await RunAtAsync(host, first);
        var mail = Assert.Single(Reminders(host));
        Assert.Equal("owner@example.org", mail.To);
        Assert.Equal("[5 Critical] 2 open reminder(s) in Acme", mail.Subject);
        Assert.Contains("[5 Critical] #2 PostgreSQL in Platform Ops", mail.Text, StringComparison.Ordinal);
        Assert.Contains("[4 High] #3 PostgreSQL in Platform Ops", mail.Text, StringComparison.Ordinal);
        Assert.Contains("https://ardea.test/o/acme/reminders/2", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("#4 ", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("#6 ", mail.Text, StringComparison.Ordinal);
        Assert.Equal(["reminder.due|[5 Critical] 2 open reminder(s) in Acme|/o/acme/reminders"], await host.RowsAsync("SELECT kind || '|' || title || '|' || link FROM notify.notification"));
        Assert.Equal(TimeSpan.FromHours(8), TimeZoneInfo.ConvertTime(await NextRunAsync(host), DigestSchedule.Zone("Europe/Zurich")).TimeOfDay);

        // The sweep again the same morning: the run is done.
        host.Time.Advance(TimeSpan.FromHours(1));
        await host.RunAsync(new FindDueDigests(), organization: null);
        Assert.Empty(await host.RunJobsAsync());
        Assert.Single(Reminders(host));

        // The next weekday: level 5 again, level 4 waits for its week. Then it is done, and nothing more comes.
        await RunAtAsync(host, await NextRunAsync(host));
        Assert.Equal(2, Reminders(host).Count);
        Assert.Equal("[5 Critical] 1 open reminder(s) in Acme", Reminders(host)[1].Subject);
        await host.ExecuteAsync($"UPDATE assess.remediation SET status = 'closed', closed_at = now(), close_reason = 'remediated' WHERE id = '{critical}'");
        await RunAtAsync(host, await NextRunAsync(host));
        Assert.Equal(2, Reminders(host).Count);
        Assert.Equal(2L, await s.CountAsync("SELECT count(*) FROM notify.reminder_state"));
    }

    [Fact]
    public async Task A_reminder_done_before_its_message_goes_out_is_left_out_and_a_message_left_empty_is_suppressed()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("Critical", 5, ["action.raised"], Scenario.Target("email"));
        var action = await s.ActionAsync(2, level: 5);
        await host.ExecuteAsync($"UPDATE assess.remediation SET opened_at = '{host.Time.GetUtcNow().AddDays(-2):O}'");
        await host.ExecuteAsync($"""
            INSERT INTO notify.reminder_schedule (organization_id, level5, level4, level3, next_run_at, changed_at)
            VALUES ('{s.Organization}', 'daily', 'weekly', 'monthly', '{host.Time.GetUtcNow().AddMinutes(-1):O}', now())
            """);

        await host.RunAsync(new SendReminders(), s.Organization);
        await host.ExecuteAsync($"UPDATE assess.remediation SET status = 'closed', closed_at = now(), close_reason = 'remediated' WHERE id = '{action}'");
        Assert.Empty(await host.RunJobsAsync());

        Assert.Empty(host.Mail!.Sent);
        Assert.Equal(["reminder suppressed None of its reminders is open any more."], await host.RowsAsync("SELECT kind || ' ' || state || ' ' || last_error FROM notify.delivery"));
    }

    [Fact]
    public async Task The_rhythm_is_set_per_organisation_by_its_admins_and_off_means_no_reminder()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host, keepDefaultRules: true);
        var path = $"{s.Path}/reminder-rhythm";
        var defaults = await NotifyTestHost.JsonAsync(await s.Owner.GetAsync(path));
        Assert.Equal("daily weekly monthly", $"{defaults.GetProperty("level5")} {defaults.GetProperty("level4")} {defaults.GetProperty("level3")}");

        var invalid = await s.Owner.PutAsync(path, new { level5 = "hourly", level4 = "weekly", level3 = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("level5", problem, StringComparison.Ordinal);
        Assert.Contains("level3", problem, StringComparison.Ordinal);

        var saved = await NotifyTestHost.JsonAsync(await s.Owner.PutAsync(path, new { level5 = "off", level4 = "off", level3 = "off" }));
        Assert.Equal("off", saved.GetProperty("level5").GetString());
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM platform.audit_event WHERE action = 'notify.reminder_rhythm_changed'"));

        using var viewer = await host.RegisterAsync("viewer@example.org", "Viewer");
        await host.AddMemberAsync(s.Organization, "viewer@example.org", "viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(path)).StatusCode);

        await s.ActionAsync(2, level: 5);
        await host.ExecuteAsync($"UPDATE assess.remediation SET opened_at = '{host.Time.GetUtcNow().AddDays(-2):O}'");
        await host.ExecuteAsync($"UPDATE notify.reminder_schedule SET next_run_at = '{host.Time.GetUtcNow().AddMinutes(-1):O}'");
        await host.ExecuteAsync("DELETE FROM platform.job");
        await host.RunAsync(new FindDueDigests(), organization: null);
        Assert.Empty(await host.RunJobsAsync());
        Assert.Empty(Reminders(host));
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.delivery WHERE kind = 'reminder'"));
    }

    /// <summary>The reminder messages; the default weekly summary comes too, empty, on Mondays.</summary>
    private static List<Platform.Email.EmailMessage> Reminders(NotifyTestHost host) =>
        [.. host.Mail!.Sent.Where(m => m.Subject.Contains("open reminder", StringComparison.Ordinal))];

    private static async Task<DateTimeOffset> NextRunAsync(NotifyTestHost host) =>
        new(await host.ScalarAsync<DateTime>("SELECT next_run_at FROM notify.reminder_schedule"), TimeSpan.Zero);

    private static async Task RunAtAsync(NotifyTestHost host, DateTimeOffset at)
    {
        host.Time.SetUtcNow(at.AddMinutes(1));
        await host.RunAsync(new FindDueDigests(), organization: null);
        Assert.Empty(await host.RunJobsAsync());
    }
}
