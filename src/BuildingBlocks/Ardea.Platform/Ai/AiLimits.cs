using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Ardea.Platform.Ai;

public sealed record AiLimits(int Parallelism, TimeSpan CallTimeout, long DailyTokens, long DailyTokensPerOrganization, int DailyRequestsPerOrganization)
{
    public const string ParallelismKey = "AI:PARALLELISM";
    public const string CallTimeoutKey = "AI:CALL_TIMEOUT_SECONDS";
    public const string DailyTokensKey = "AI:DAILY_TOKENS";
    public const string DailyTokensPerOrganizationKey = "AI:DAILY_TOKENS_PER_ORGANIZATION";
    public const string DailyRequestsPerOrganizationKey = "AI:DAILY_REQUESTS_PER_ORGANIZATION";

    public static readonly AiLimits Default = new(1, TimeSpan.FromSeconds(120), 2_000_000, 100_000, 20);

    public static readonly string[] Keys = [ParallelismKey, CallTimeoutKey, DailyTokensKey, DailyTokensPerOrganizationKey, DailyRequestsPerOrganizationKey];

    public static AiLimits FromConfiguration(IConfiguration configuration) => new(
        (int)Read(configuration, ParallelismKey, Default.Parallelism),
        TimeSpan.FromSeconds(Read(configuration, CallTimeoutKey, (long)Default.CallTimeout.TotalSeconds)),
        Read(configuration, DailyTokensKey, Default.DailyTokens),
        Read(configuration, DailyTokensPerOrganizationKey, Default.DailyTokensPerOrganization),
        (int)Read(configuration, DailyRequestsPerOrganizationKey, Default.DailyRequestsPerOrganization));

    public static DateTimeOffset DayStart(DateTimeOffset now) => new(now.UtcDateTime.Date, TimeSpan.Zero);

    private static long Read(IConfiguration configuration, string key, long fallback) =>
        long.TryParse(configuration[key], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;
}
