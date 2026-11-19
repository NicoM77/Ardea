using Ardea.Catalog.Contracts;
using Ardea.Catalog.Versions;
using static Ardea.Catalog.Tests.Versions.SchemeAssert;

namespace Ardea.Catalog.Tests.Versions;

/// <summary>Which scheme compares a record's versions, and that versions of two schemes are never ordered (master plan 6.6).</summary>
public sealed class VersionSchemeRegistryTests
{
    [Theory]
    [InlineData(null, VersionSchemeNames.Numeric)]
    [InlineData("", VersionSchemeNames.Numeric)]
    [InlineData(" custom ", VersionSchemeNames.Numeric)]
    [InlineData("npm", VersionSchemeNames.SemVer)]
    [InlineData("PYTHON", VersionSchemeNames.Pep440)]
    [InlineData("git", null)]
    [InlineData("debian", null)]
    public void Version_type_of_a_record_picks_its_scheme_and_git_or_unknown_types_none(string? versionType, string? scheme) =>
        Assert.Equal(scheme, VersionSchemeRegistry.Instance.Find(versionType)?.Name);

    [Theory]
    [InlineData("Red Hat:enterprise_linux:9", VersionSchemeNames.Rpm)]
    [InlineData("openSUSE:Leap 15.5", VersionSchemeNames.Rpm)]
    [InlineData("golang", VersionSchemeNames.Go)]
    [InlineData("Debian:12", null)]
    [InlineData("Alpine", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Ecosystem_qualified_with_a_release_uses_the_ecosystems_scheme_and_unknown_ones_none(string? ecosystem, string? scheme) =>
        Assert.Equal(scheme, VersionSchemeRegistry.Instance.ForEcosystem(ecosystem)?.Name);

    [Fact]
    public void Versions_of_two_schemes_are_never_ordered()
    {
        var semver = Parse(VersionSchemeNames.SemVer, "1.0.0");
        var maven = Parse(VersionSchemeNames.Maven, "1.0.0");

        Assert.Throws<ArgumentException>(() => semver.CompareTo(maven));
        Assert.Throws<ArgumentException>(() => semver < maven);
        Assert.False(semver.Equals(maven));
        Assert.True(semver != maven);
    }

    [Fact]
    public void Spellings_of_one_version_are_one_key()
    {
        var one = Parse(VersionSchemeNames.Maven, "1");
        var oneZero = Parse(VersionSchemeNames.Maven, "1.0");

        Assert.True(one == oneZero);
        Assert.Single(new HashSet<ParsedVersion> { one, oneZero });
        Assert.Equal(1, one.CompareTo(null));
        Assert.False(one == null);
    }
}
