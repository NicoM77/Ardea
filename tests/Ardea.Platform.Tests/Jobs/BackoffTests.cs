using Ardea.Platform.Jobs;

namespace Ardea.Platform.Tests.Jobs;

public sealed class BackoffTests
{
    [Theory]
    [InlineData(1, 8, 12)]
    [InlineData(2, 16, 24)]
    [InlineData(4, 64, 96)]
    public void Delay_doubles_per_attempt_within_twenty_percent(int attempt, double min, double max)
    {
        Assert.InRange(Backoff.After(attempt, 0).TotalSeconds, min, max);
        Assert.InRange(Backoff.After(attempt, 0.999).TotalSeconds, min, max);
    }

    [Fact]
    public void Delay_never_exceeds_one_hour()
    {
        Assert.Equal(Backoff.Cap, Backoff.After(30, 0.999));
    }
}
