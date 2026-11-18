using static Ardea.Catalog.Tests.Versions.SchemeAssert;

namespace Ardea.Catalog.Tests.Versions;

/// <summary>PEP 440 for PyPI (master plan 6.6).</summary>
public sealed class Pep440SchemeTests
{
    [Theory]
    // The order of all suffixes from PEP 440, «Summary of permitted suffixes and relative ordering».
    [InlineData("1.0.dev456 < 1.0a1 < 1.0a2.dev456 < 1.0a12.dev456 < 1.0a12 < 1.0b1.dev456 < 1.0b2 < 1.0b2.post345.dev456 < 1.0b2.post345 < 1.0rc1.dev456 < 1.0rc1 < 1.0 < 1.0+abc.5 < 1.0+abc.7 < 1.0+5 < 1.0.post456.dev34 < 1.0.post456 < 1.0.15 < 1.1.dev1")]
    // An epoch outranks every release.
    [InlineData("2013.10 < 2014.04 < 1!1.0 < 1!1.1")]
    // django: GHSA-2gwj-7jmv-h26r fixed in 3.2.23, 4.1.13, 4.2.7; jinja2 3.1.3 against its release candidate.
    [InlineData("3.2.22 < 3.2.23 < 4.0a1 < 4.0 < 4.1.13 < 4.2.7")]
    [InlineData("3.0.0rc1 < 3.0.0rc2 < 3.0.0 < 3.1.3")]
    // A dev release sorts before the alpha of the same release.
    [InlineData("4.0 < 4.1.dev20220801 < 4.1a1 < 4.1")]
    public void Versions_follow_pep440_order(string ascending) => Ascending("pep440", ascending);

    [Theory]
    [InlineData("1.0", "1.0.0")]
    [InlineData("4.0", "4.0.0.0")]
    [InlineData("1.0-RC1", "1.0rc1")]
    [InlineData("1.0.0-alpha.1", "1.0.0a1")]
    [InlineData("1.0c1", "1.0rc1")]
    [InlineData("1.0-1", "1.0.post1")]
    [InlineData("v1.0", "1.0")]
    [InlineData("0!1.0", "1.0")]
    public void Spellings_of_one_version_are_equal(string left, string right) => Same("pep440", left, right);

    [Theory]
    [InlineData("1.0-RC1", "1.0rc1")]
    [InlineData("1.0.0-alpha.1", "1.0.0a1")]
    [InlineData("1.0preview2", "1.0rc2")]
    [InlineData("1.0-1", "1.0.post1")]
    [InlineData("1.0.post", "1.0.post0")]
    [InlineData("1.0-dev", "1.0.dev0")]
    [InlineData("0!2.0", "2.0")]
    [InlineData("1!2.0", "1!2.0")]
    [InlineData("01.002", "1.2")]
    [InlineData("1.0+Ubuntu-1", "1.0+ubuntu.1")]
    public void Normal_form_is_pep440s(string text, string normalized) => Assert.Equal(normalized, Parse("pep440", text).Normalized);

    [Theory]
    [InlineData(">=2.31.0")]
    [InlineData("~=4.2")]
    [InlineData("latest")]
    [InlineData("1.0-")]
    [InlineData("")]
    public void Specifiers_and_words_are_no_versions(string text) => Assert.False(Scheme("pep440").IsVersion(text));
}
