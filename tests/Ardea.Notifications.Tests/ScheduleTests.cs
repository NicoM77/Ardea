using Ardea.Notifications.Delivering;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;

namespace Ardea.Notifications.Tests;

public sealed class ScheduleTests
{
    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void A_digest_at_an_hour_the_clocks_skip_comes_when_the_clocks_have_moved_on_and_once_on_the_day_the_hour_repeats()
    {
        Assert.Equal(Utc("2026-03-29T01:00:00Z"), DigestSchedule.Next("30 2 * * *", "Europe/Zurich", Utc("2026-03-28T23:00:00Z")));
        Assert.Equal(Utc("2026-03-30T00:30:00Z"), DigestSchedule.Next("30 2 * * *", "Europe/Zurich", Utc("2026-03-29T01:00:00Z")));

        var first = DigestSchedule.Next("30 2 * * *", "Europe/Zurich", Utc("2026-10-24T23:00:00Z"));
        Assert.Equal(Utc("2026-10-25T00:30:00Z"), first);
        Assert.Equal(Utc("2026-10-26T01:30:00Z"), DigestSchedule.Next("30 2 * * *", "Europe/Zurich", first!.Value));
    }

    [Theory]
    [InlineData("Europe/Zurich", "2026-03-27T08:00:00Z", "2026-03-30T06:00:00Z")]
    [InlineData("Europe/Zurich", "2026-10-23T07:00:00Z", "2026-10-26T07:00:00Z")]
    [InlineData("America/New_York", "2026-03-06T13:00:00Z", "2026-03-09T12:00:00Z")]
    [InlineData("America/New_York", "2026-10-30T12:00:00Z", "2026-11-02T13:00:00Z")]
    [InlineData("Australia/Sydney", "2026-04-02T21:00:00Z", "2026-04-05T22:00:00Z")]
    public void The_reminders_run_on_weekdays_at_eight_local_time_across_a_change_of_the_clocks(string zone, string after, string next)
    {
        var at = DigestSchedule.Next(ReminderRhythms.Schedule, zone, Utc(after));

        Assert.Equal(Utc(next), at);
        var local = TimeZoneInfo.ConvertTime(at!.Value, DigestSchedule.Zone(zone));
        Assert.Equal((DayOfWeek.Monday, 8, 0), (local.DayOfWeek, local.Hour, local.Minute));
    }

    [Fact]
    public void A_zone_the_instance_does_not_know_counts_as_UTC_and_a_schedule_that_never_comes_has_no_next_time()
    {
        Assert.Equal(TimeZoneInfo.Utc, DigestSchedule.Zone("Mars/Olympus_Mons"));
        Assert.Equal(Utc("2026-11-20T08:00:00Z"), DigestSchedule.Next("0 8 * * *", "Mars/Olympus_Mons", Utc("2026-11-20T07:00:00Z")));
        Assert.Null(DigestSchedule.Next("0 8 30 2 *", "UTC", Utc("2026-11-20T07:00:00Z")));
        foreach (var cron in new[] { "", "   ", "every morning", "61 8 * * *", "0 8 * * * *" })
        {
            Assert.False(DigestSchedule.TryParse(cron, out _), cron);
            Assert.Null(DigestSchedule.Next(cron, "UTC", Utc("2026-11-20T07:00:00Z")));
        }
    }

    [Fact]
    public void A_monthly_reminder_from_the_last_of_a_month_is_due_on_the_last_of_a_shorter_one()
    {
        var since = Utc("2026-01-31T10:00:00Z");

        Assert.False(ReminderRhythms.IsDue(ReminderRhythms.Monthly, since, null, Utc("2026-02-27T21:00:00Z")));
        Assert.True(ReminderRhythms.IsDue(ReminderRhythms.Monthly, since, null, Utc("2026-02-28T07:00:00Z")));
        Assert.False(ReminderRhythms.IsDue(ReminderRhythms.Monthly, since, Utc("2026-02-28T07:00:00Z"), Utc("2026-03-27T07:00:00Z")));
        Assert.True(ReminderRhythms.IsDue(ReminderRhythms.Monthly, since, Utc("2026-02-28T07:00:00Z"), Utc("2026-03-28T07:00:00Z")));
    }

    [Fact]
    public void A_weekly_reminder_raised_after_eight_is_due_at_eight_a_week_later_also_when_the_clocks_moved_forward_in_between()
    {
        var raised = Utc("2026-03-23T08:00:00Z");
        var run = DigestSchedule.Next(ReminderRhythms.Schedule, "Europe/Zurich", Utc("2026-03-27T08:00:00Z"))!.Value;

        Assert.Equal(Utc("2026-03-30T06:00:00Z"), run);
        Assert.True(ReminderRhythms.IsDue(ReminderRhythms.Weekly, raised, null, run));
        Assert.False(ReminderRhythms.IsDue(ReminderRhythms.Weekly, raised, null, Utc("2026-03-27T06:00:00Z")));
    }

    [Fact]
    public void Below_level_3_nothing_reminds_and_a_reminder_reaches_people_not_machines()
    {
        var schedule = new ReminderSchedule { Level5 = ReminderRhythms.Off, Level4 = ReminderRhythms.Daily, Level3 = ReminderRhythms.Weekly };

        Assert.Equal(
            [ReminderRhythms.Off, ReminderRhythms.Daily, ReminderRhythms.Weekly, ReminderRhythms.Off],
            new[] { 5, 4, 3, 2 }.Select(level => ReminderRhythms.Of(schedule, level)));
        Assert.False(ReminderRhythms.IsDue(ReminderRhythms.Off, Utc("2020-01-01T00:00:00Z"), null, Utc("2026-11-20T07:00:00Z")));
        Assert.Equal([true, true, true, false], new[] { Channels.InApp, Channels.Email, Channels.Push, Channels.Webhook }.Select(ReminderRhythms.Reaches));
    }
}
