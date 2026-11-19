using Ardea.Assets.Persistence;

namespace Ardea.Assets.Tests;

/// <summary>
/// The precision a version has when nobody states it (master plan 17.1, ADR 046); the migration <c>StackAndPrecision</c>
/// backfills existing components by the same rule.
/// </summary>
public sealed class VersionPrecisionTests
{
    [Theory]
    [InlineData(null, null, "manual", "none")]
    [InlineData("  ", null, "manual", "none")]
    [InlineData("17", null, "manual", "line")]
    [InlineData("8.x", null, "manual", "line")]
    [InlineData("8.3.*", null, "manual", "line")]
    [InlineData("8.3", null, "manual", "exact")]
    [InlineData("17.11", null, "manual", "exact")]
    [InlineData("6.12.58", null, "manual", "exact")]
    [InlineData("2.4.62-1ubuntu1", null, "manual", "exact")]
    [InlineData("10.0", null, "repository", "line")]
    [InlineData("10.0.404", null, "repository", "exact")]
    [InlineData("13.0", "pkg:nuget/Newtonsoft.Json@13.0", "repository", "exact")]
    [InlineData(null, "pkg:npm/express", "repository", "none")]
    public void A_version_without_a_stated_precision_gets_the_one_that_follows_from_it(string? version, string? purl, string origin, string expected) =>
        Assert.Equal(expected, VersionPrecisions.Of(version, purl, origin));

    [Theory]
    [InlineData("none", null, true)]
    [InlineData("none", "17", false)]
    [InlineData("line", "8.3", true)]
    [InlineData("exact", null, false)]
    [InlineData("line", " ", false)]
    public void A_stated_precision_is_none_exactly_without_a_version(string precision, string? version, bool fits) =>
        Assert.Equal(fits, VersionPrecisions.Fits(precision, version));
}
