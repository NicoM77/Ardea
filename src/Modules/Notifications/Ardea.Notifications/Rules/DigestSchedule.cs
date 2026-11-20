using Cronos;

namespace Ardea.Notifications.Rules;

internal static class DigestSchedule
{
    public static bool TryParse(string? cron, out CronExpression? expression)
    {
        expression = null;
        if (string.IsNullOrWhiteSpace(cron))
        {
            return false;
        }

        try
        {
            expression = CronExpression.Parse(cron.Trim(), CronFormat.Standard);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }

    public static DateTimeOffset? Next(string cron, string timeZone, DateTimeOffset after) =>
        TryParse(cron, out var expression) ? expression!.GetNextOccurrence(after, Zone(timeZone))?.ToUniversalTime() : null;

    public static TimeZoneInfo Zone(string timeZone) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone) ? zone : TimeZoneInfo.Utc;
}
