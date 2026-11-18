using static Ardea.Catalog.Tests.Versions.SchemeAssert;

namespace Ardea.Catalog.Tests.Versions;

/// <summary>SemVer 2.0 for npm and Go (master plan 6.6).</summary>
public sealed class SemVerSchemeTests
{
    [Theory]
    // Precedence example of the SemVer 2.0 specification, item 11.
    [InlineData("1.0.0-alpha < 1.0.0-alpha.1 < 1.0.0-alpha.beta < 1.0.0-beta < 1.0.0-beta.2 < 1.0.0-beta.11 < 1.0.0-rc.1 < 1.0.0")]
    [InlineData("1.0.0 < 2.0.0 < 2.1.0 < 2.1.1")]
    // express: GHSA-rv95-896h-c2vc fixed in 4.19.2 and 5.0.0-beta.3.
    [InlineData("4.19.1 < 4.19.2 < 5.0.0-alpha.1 < 5.0.0-beta.1 < 5.0.0-beta.3 < 5.0.0")]
    // next: canary builds are pre-releases of the next patch.
    [InlineData("14.1.0 < 14.1.1-canary.0 < 14.1.1-canary.12 < 14.1.1")]
    // Numeric identifiers compare by value, also beyond 64 bits.
    [InlineData("1.0.0-2 < 1.0.0-10 < 1.0.0-99999999999999999999 < 1.0.0-a")]
    public void Npm_versions_follow_semver_precedence(string ascending) => Ascending("semver", ascending);

    [Theory]
    // Pseudo-versions are pre-releases: before the tagged version they lead up to.
    [InlineData("v0.0.0-20220127200216-cd36cc0744dd < v0.0.0-20230101000000-abcdefabcdef < v0.1.0")]
    [InlineData("v1.2.3 < v1.2.4-0.20191109021931-daa7c04131f5 < v1.2.4")]
    [InlineData("v1.2.4-pre < v1.2.4-pre.0.20191109021931-daa7c04131f5 < v1.2.4")]
    // golang.org/x/net: GO-2023-2102 fixed in 0.17.0; stdlib release candidates.
    [InlineData("0.16.0 < 0.17.0 < v0.18.0")]
    [InlineData("v1.19.0-0 < v1.19.0-rc.1 < v1.19.0 < v1.19.1")]
    public void Go_versions_follow_semver_with_prefix_and_pseudo_versions(string ascending) => Ascending("go", ascending);

    [Theory]
    [InlineData("go", "v20.10.24+incompatible", "20.10.24")]
    [InlineData("semver", "1.0.0+20130313144700", "1.0.0")]
    [InlineData("semver", "v1.2.3", "1.2.3")]
    [InlineData("semver", "8.2", "8.2.0")]
    [InlineData("semver", "1.02.3", "1.2.3")]
    public void Prefix_metadata_and_missing_numbers_do_not_change_the_version(string scheme, string left, string right) =>
        Same(scheme, left, right);

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.02.03-beta.1+build", "1.2.3-beta.1")]
    [InlineData("8.2", "8.2.0")]
    public void Normal_form_drops_prefix_leading_zeros_and_metadata(string text, string normalized) =>
        Assert.Equal(normalized, Parse("semver", text).Normalized);

    [Theory]
    [InlineData("latest")]
    [InlineData("^4.17.1")]
    [InlineData("~1.2")]
    [InlineData(">=1.0.0")]
    [InlineData("1.2.3 - 2.0.0")]
    [InlineData("master")]
    [InlineData("")]
    public void Ranges_tags_and_branches_are_no_versions(string text) =>
        Assert.False(Scheme("semver").IsVersion(text));
}
