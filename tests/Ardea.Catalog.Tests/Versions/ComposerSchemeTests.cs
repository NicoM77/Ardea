using static Ardea.Catalog.Tests.Versions.SchemeAssert;

namespace Ardea.Catalog.Tests.Versions;

/// <summary>Packagist: Composer's version parser and PHP's version_compare (master plan 6.6).</summary>
public sealed class ComposerSchemeTests
{
    [Theory]
    // Stability order of Composer: dev < alpha < beta < RC < stable < patch.
    [InlineData("1.0.0-dev < 1.0.0-alpha1 < 1.0.0-beta < 1.0.0-beta1 < 1.0.0-beta2 < 1.0.0-RC1 < 1.0.0 < 1.0.0-patch1 < 1.0.1")]
    // A trailing -dev is the development state of that pre-release.
    [InlineData("1.0.0-beta2-dev < 1.0.0-beta2 < 1.0.0-beta3")]
    // Four numbers.
    [InlineData("1.2.3 < 1.2.3.4 < 1.2.4")]
    // guzzlehttp/guzzle: GHSA-w248-ffj2-4v5q fixed in 7.4.5; laravel/framework 9.52.16 before 10.
    [InlineData("v7.4.4 < 7.4.5 < v7.5.0")]
    [InlineData("9.52.16 < 9.52.17 < 10.0.0-RC1 < 10.0.0")]
    // In SemVer «RC1» would sort before «beta1» (upper case first); Composer does not care about case.
    [InlineData("4.0.0-beta1 < 4.0.0-RC1 < 4.0.0-rc2")]
    public void Versions_follow_composer_order(string ascending) => Ascending("packagist", ascending);

    [Theory]
    [InlineData("v2.4.1", "2.4.1")]
    [InlineData("9.52.17.0", "9.52.17")]
    [InlineData("3.12", "3.12.0")]
    [InlineData("1.0.0-RC1", "1.0.0-rc1")]
    [InlineData("1.0.0-b2", "1.0.0-beta2")]
    [InlineData("1.0.0-a1", "1.0.0-alpha.1")]
    [InlineData("1.0.0-pl3", "1.0.0-p3")]
    public void Spellings_of_one_version_are_equal(string left, string right) => Same("packagist", left, right);

    [Theory]
    [InlineData("v1.2", "1.2.0.0")]
    [InlineData("1.0.0-b2", "1.0.0.0-beta2")]
    [InlineData("1.0.0RC1", "1.0.0.0-RC1")]
    [InlineData("2.0.0-dev", "2.0.0.0-dev")]
    public void Normal_form_is_composers(string text, string normalized) => Assert.Equal(normalized, Parse("packagist", text).Normalized);

    [Theory]
    [InlineData("dev-main")]
    [InlineData("2.x-dev")]
    [InlineData("^6.5")]
    [InlineData("~7.0")]
    [InlineData("*")]
    [InlineData("")]
    public void Branches_and_constraints_are_no_versions(string text) => Assert.False(Scheme("packagist").IsVersion(text));
}
