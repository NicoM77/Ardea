using Ardea.Catalog.Versions;
using static Ardea.Catalog.Tests.Versions.SchemeAssert;

namespace Ardea.Catalog.Tests.Versions;

/// <summary>RPM versions (master plan 6.6); the rpmvercmp table comes from rpm's own test suite (rpmvercmp.at).</summary>
public sealed class RpmSchemeTests
{
    [Theory]
    [InlineData("1.0", "1.0", 0)]
    [InlineData("1.0", "2.0", -1)]
    [InlineData("2.0", "2.0.1", -1)]
    [InlineData("2.0.1a", "2.0.1", 1)]
    [InlineData("5.5p1", "5.5p2", -1)]
    [InlineData("5.5p10", "5.5p1", 1)]
    [InlineData("10xyz", "10.1xyz", -1)]
    [InlineData("xyz10", "xyz10.1", -1)]
    [InlineData("xyz.4", "8", -1)]
    [InlineData("5.6p1", "6.5p1", -1)]
    [InlineData("6.0.rc1", "6.0", 1)]
    [InlineData("10b2", "10a1", 1)]
    [InlineData("1.0aa", "1.0a", 1)]
    [InlineData("10.0001", "10.1", 0)]
    [InlineData("10.0001", "10.0039", -1)]
    [InlineData("4.999.9", "5.0", -1)]
    [InlineData("20101121", "20101122", -1)]
    [InlineData("2.0", "2_0", 0)]
    [InlineData("a+", "a_", 0)]
    [InlineData("1.0~rc1", "1.0", -1)]
    [InlineData("1.0~rc1", "1.0~rc2", -1)]
    [InlineData("1.0~rc1~git123", "1.0~rc1", -1)]
    [InlineData("1.0^", "1.0", 1)]
    [InlineData("1.0^git1", "1.0", 1)]
    [InlineData("1.0^git1", "1.0^git2", -1)]
    [InlineData("1.0^git1", "1.01", -1)]
    [InlineData("1.0^20160101", "1.0.1", -1)]
    [InlineData("1.0~rc1^git1", "1.0~rc1", 1)]
    [InlineData("1.0^git1~pre", "1.0^git1", -1)]
    public void Rpmvercmp_matches_rpm(string left, string right, int expected)
    {
        Assert.Equal(expected, RpmScheme.Compare(left, right));
        Assert.Equal(-expected, RpmScheme.Compare(right, left));
    }

    [Theory]
    // openssl in RHEL 9 errata; release numbers with dist tags.
    [InlineData("3.0.7-16.el9 < 3.0.7-18.el9 < 3.0.7-25.el9_3 < 3.0.7-27.el9")]
    [InlineData("1:1.1.1k-7.el8_6 < 1:1.1.1k-9.el8_7 < 1:1.1.1k-12.el8_9")]
    // The epoch outranks version and release.
    [InlineData("9.9-1 < 1:1.0-1")]
    public void Epoch_version_release_follow_rpm(string ascending) => Ascending("rpm", ascending);

    [Theory]
    [InlineData("0:1.2.3-4.el8", "1.2.3-4.el8")]
    // As in rpm's comparison of dependencies, a missing release is not compared.
    [InlineData("1.2.3", "1.2.3-4")]
    public void Missing_epoch_is_zero_and_missing_release_is_not_compared(string left, string right) => Same("rpm", left, right);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:2:3")]
    [InlineData(":1.0")]
    [InlineData("1.0-")]
    public void Malformed_versions_are_rejected(string text) => Assert.False(Scheme("rpm").IsVersion(text));
}
