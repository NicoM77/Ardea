namespace Ardea.Platform.Jobs;

public static class Backoff
{
    public static readonly TimeSpan Base = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan Cap = TimeSpan.FromHours(1);

    public static TimeSpan After(int attempt, double jitter) => After(attempt, jitter, Base);

    public static TimeSpan After(int attempt, double jitter, TimeSpan @base)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(jitter);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(jitter, 1);

        var exponent = Math.Min(attempt - 1, 20);
        var delay = Math.Min(@base.TotalSeconds * Math.Pow(2, exponent), Cap.TotalSeconds);
        return TimeSpan.FromSeconds(Math.Min(delay * (0.8 + (0.4 * jitter)), Cap.TotalSeconds));
    }
}
