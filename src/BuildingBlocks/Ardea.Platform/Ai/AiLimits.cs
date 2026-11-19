using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Ardea.Platform.Ai;

/// <summary>
/// The limits on language model calls (master plan 7.8 «Grenzen»): <c>ARDEA_AI__PARALLELISM</c>,
/// <c>ARDEA_AI__CALL_TIMEOUT_SECONDS</c>, <c>ARDEA_AI__DAILY_TOKENS</c>, <c>ARDEA_AI__DAILY_TOKENS_PER_ORGANIZATION</c>
/// and <c>ARDEA_AI__DAILY_REQUESTS_PER_ORGANIZATION</c>. A day is a calendar day in UTC. When a quota is used up, the
/// rules alone apply until the next day.
/// </summary>
/// <param name="Parallelism">Assessments that wait for a model at the same time, per worker.</param>
/// <param name="CallTimeout">The time limit of one call; a call over it counts as failed.</param>
/// <param name="DailyTokens">Tokens in and out per day for the whole instance.</param>
/// <param name="DailyTokensPerOrganization">Tokens in and out per day for one organisation.</param>
/// <param name="DailyRequestsPerOrganization">Assessments people of one organisation may ask for per day. Their jobs
/// run before the system's in every organisation; the cap keeps one organisation from holding the others back.</param>
public sealed record AiLimits(int Parallelism, TimeSpan CallTimeout, long DailyTokens, long DailyTokensPerOrganization, int DailyRequestsPerOrganization)
{
    public const string ParallelismKey = "AI:PARALLELISM";
    public const string CallTimeoutKey = "AI:CALL_TIMEOUT_SECONDS";
    public const string DailyTokensKey = "AI:DAILY_TOKENS";
    public const string DailyTokensPerOrganizationKey = "AI:DAILY_TOKENS_PER_ORGANIZATION";
    public const string DailyRequestsPerOrganizationKey = "AI:DAILY_REQUESTS_PER_ORGANIZATION";

    /// <summary>
    /// One call at a time suits a local model on a CPU or a single GPU; two minutes leave room for a slow one
    /// (master plan 7.10: 10 to 20 seconds per call). A day of 100 000 tokens covers about 40 assessments of one
    /// organisation, the most the spike week asks for in a whole week; 20 requests by people a day leave room for a
    /// team that works through its alerts without letting one organisation fill a worker's day.
    /// </summary>
    public static readonly AiLimits Default = new(1, TimeSpan.FromSeconds(120), 2_000_000, 100_000, 20);

    public static readonly string[] Keys = [ParallelismKey, CallTimeoutKey, DailyTokensKey, DailyTokensPerOrganizationKey, DailyRequestsPerOrganizationKey];

    public static AiLimits FromConfiguration(IConfiguration configuration) => new(
        (int)Read(configuration, ParallelismKey, Default.Parallelism),
        TimeSpan.FromSeconds(Read(configuration, CallTimeoutKey, (long)Default.CallTimeout.TotalSeconds)),
        Read(configuration, DailyTokensKey, Default.DailyTokens),
        Read(configuration, DailyTokensPerOrganizationKey, Default.DailyTokensPerOrganization),
        (int)Read(configuration, DailyRequestsPerOrganizationKey, Default.DailyRequestsPerOrganization));

    /// <summary>The start of the quota day that contains <paramref name="now"/>.</summary>
    public static DateTimeOffset DayStart(DateTimeOffset now) => new(now.UtcDateTime.Date, TimeSpan.Zero);

    private static long Read(IConfiguration configuration, string key, long fallback) =>
        long.TryParse(configuration[key], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;
}
