using static Ardea.Catalog.Tests.Versions.SchemeAssert;

namespace Ardea.Catalog.Tests.Versions;

public sealed class NumericSchemeTests
{
    [Theory]
    [InlineData("2.4.48 < 2.4.49 < 2.4.50 < 2.4.51")]
    [InlineData("1.1.1 < 1.1.1a < 1.1.1k < 1.1.1w < 1.1.2")]
    [InlineData("9.3 < 9.3p1 < 9.3p2 < 9.4 < 9.4p1")]
    [InlineData("2.0.0-dev < 2.0.0-alpha < 2.0.0-beta1 < 2.0.0-beta2 < 2.0.0-rc1 < 2.0.0 < 2.0.0-sp1 < 2.0.1")]
    [InlineData("10.0.17763.1 < 10.0.17763.1234 < 10.0.19041.1")]
    [InlineData("2022 < 2023.1 < 2024")]
    [InlineData("9.6.24 < 10.0 < 16.3 < 16.4 < 17.0")]
    public void Numbers_compare_one_by_one_and_suffixes_by_kind(string ascending) => Ascending("numeric", ascending);

    [Theory]
    [InlineData("2.4", "2.4.0")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.0+build5", "1.0")]
    [InlineData("8.0.01", "8.0.1")]
    public void Missing_zeros_prefix_and_build_metadata_do_not_count(string left, string right) => Same("numeric", left, right);

    [Theory]
    [InlineData("unspecified")]
    [InlineData("n/a")]
    [InlineData("*")]
    [InlineData("2.4.x")]
    [InlineData("<= 2.4")]
    [InlineData("all versions")]
    [InlineData("")]
    public void Words_placeholders_and_operators_are_no_versions(string text) => Assert.False(Scheme("numeric").IsVersion(text));
}
