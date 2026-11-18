using static Ardea.Catalog.Tests.Versions.SchemeAssert;

namespace Ardea.Catalog.Tests.Versions;

/// <summary>NuGet: SemVer with a fourth number and NuGet's normalisation (master plan 6.6).</summary>
public sealed class NuGetSchemeTests
{
    [Theory]
    [InlineData("1.0.0-alpha < 1.0.0-beta < 1.0.0-rc.1 < 1.0.0 < 1.0.0.1 < 1.0.1")]
    // Newtonsoft.Json: GHSA-5crp-9r3c-p9vr fixed in 13.0.1.
    [InlineData("12.0.3 < 13.0.1 < 13.0.2")]
    // System.Text.Encodings.Web: .NET 5 previews before the release.
    [InlineData("4.7.1 < 5.0.0-preview.8.20407.11 < 5.0.0-rc.2.20475.5 < 5.0.0 < 5.0.1")]
    [InlineData("2.1.0.9 < 2.1.0.10 < 2.1.1")]
    public void Versions_follow_nuget_order(string ascending) => Ascending("nuget", ascending);

    [Theory]
    [InlineData("13.0.1", "13.0.1.0")]
    [InlineData("1.0", "1.0.0")]
    [InlineData("1.0.0-RC1", "1.0.0-rc1")]
    [InlineData("1.0.0+commit.abc", "1.0.0")]
    public void Fourth_zero_case_and_metadata_do_not_change_the_version(string left, string right) => Same("nuget", left, right);

    [Theory]
    [InlineData("1.0", "1.0.0")]
    [InlineData("1.0.0.0", "1.0.0")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData("01.2.03", "1.2.3")]
    public void Normal_form_is_nugets(string text, string normalized) => Assert.Equal(normalized, Parse("nuget", text).Normalized);

    [Theory]
    [InlineData("13.0.*")]
    [InlineData("[5.0.0, 5.2.0)")]
    [InlineData("latest")]
    [InlineData("")]
    public void Floating_versions_and_ranges_are_no_versions(string text) => Assert.False(Scheme("nuget").IsVersion(text));
}
