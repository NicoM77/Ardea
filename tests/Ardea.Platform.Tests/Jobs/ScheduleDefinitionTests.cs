using Ardea.Platform.Jobs;

namespace Ardea.Platform.Tests.Jobs;

public sealed class ScheduleDefinitionTests
{
    private static ScheduleDefinition Daily(string cron, string zone) => new("test", "test.kind", cron, zone, (_, _) => Task.CompletedTask);

    [Fact]
    public void Daily_run_follows_local_time_across_daylight_saving()
    {
        var schedule = Daily("0 7 * * *", "Europe/Zurich");

        var winter = schedule.NextAfter(new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero));
        var summer = schedule.NextAfter(winter);

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 5, 0, 0, TimeSpan.Zero), winter);
        Assert.Equal(new DateTimeOffset(2026, 3, 30, 5, 0, 0, TimeSpan.Zero), summer);
    }

    [Fact]
    public void Run_in_a_skipped_hour_moves_to_the_next_valid_time()
    {
        var schedule = Daily("30 2 * * *", "Europe/Zurich");

        var next = schedule.NextAfter(new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero), next);
    }
}
