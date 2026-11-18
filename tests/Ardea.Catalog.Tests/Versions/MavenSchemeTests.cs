using static Ardea.Catalog.Tests.Versions.SchemeAssert;

namespace Ardea.Catalog.Tests.Versions;

/// <summary>Maven ComparableVersion (master plan 6.6); the sequences come from ComparableVersionTest of Maven 3.6.</summary>
public sealed class MavenSchemeTests
{
    [Theory]
    // VERSIONS_QUALIFIER of ComparableVersionTest.
    [InlineData("1-alpha2snapshot < 1-alpha2 < 1-alpha-123 < 1-beta-2 < 1-beta123 < 1-m2 < 1-m11 < 1-rc < 1-cr2 < 1-rc123 < 1-SNAPSHOT < 1 < 1-sp < 1-sp2 < 1-sp123 < 1-abc < 1-def < 1-pom-1 < 1-1-snapshot < 1-1 < 1-2 < 1-123")]
    // VERSIONS_NUMBER of ComparableVersionTest.
    [InlineData("2.0 < 2-1 < 2.0.a < 2.0.0.a < 2.0.2 < 2.0.123 < 2.1.0 < 2.1-a < 2.1b < 2.1-c < 2.1-1 < 2.1.0.1 < 2.2 < 2.123 < 11.a2 < 11.a11 < 11.b2 < 11.b11 < 11.m2 < 11.m11 < 11 < 11.a < 11b < 11c < 11m")]
    // log4j-core: GHSA-jfh8-c2jp-5v3q introduced in 2.0-beta9, fixed in 2.12.2 and 2.16.0.
    [InlineData("2.0-beta8 < 2.0-beta9 < 2.0-rc1 < 2.0 < 2.12.1 < 2.12.2 < 2.14.1 < 2.15.0 < 2.16.0")]
    // jackson-databind micro patches; Hibernate «.CR1» before «.Final».
    [InlineData("2.13.2 < 2.13.2.1 < 2.13.3")]
    [InlineData("6.1.0.Beta2 < 6.1.0.CR1 < 6.1.0.Final < 6.1.1.Final")]
    public void Versions_follow_comparable_version(string ascending) => Ascending("maven", ascending);

    [Theory]
    [InlineData("1", "1.0")]
    [InlineData("1", "1.0.0")]
    [InlineData("2.15", "2.15.0")]
    [InlineData("1-ga", "1")]
    [InlineData("1.0.0.Final", "1.0.0")]
    [InlineData("5.2.25.RELEASE", "5.2.25")]
    [InlineData("1.0-RC1", "1.0-cr1")]
    [InlineData("1a1", "1-alpha-1")]
    [InlineData("1x", "1-x")]
    public void Spellings_of_one_version_are_equal(string left, string right) => Same("maven", left, right);

    [Theory]
    [InlineData("${spring.version}")]
    [InlineData("[2.12,2.13)")]
    [InlineData("LATEST")]
    [InlineData("")]
    public void Properties_and_ranges_are_no_versions(string text) => Assert.False(Scheme("maven").IsVersion(text));
}
