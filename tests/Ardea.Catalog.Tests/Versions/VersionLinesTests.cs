using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Tests.Versions;

/// <summary>Release lines read from leading numbers, independent of how many parts a version is written with.</summary>
public sealed class VersionLinesTests
{
    [Theory]
    [InlineData("6.3.4", "6.3")]
    [InlineData("16.4", "16.4")]
    [InlineData("16.4.0", "16.4")]
    [InlineData("6.3.1.0", "6.3")]
    [InlineData("157.0.8102.50", "157.0.8102")]
    [InlineData("v11.0.0-M2", "11.0")]
    [InlineData("26", "26.0")]
    [InlineData("latest", "")]
    public void Line_is_the_canonical_version_without_its_last_part(string version, string line) =>
        Assert.Equal(line, string.Join('.', VersionLines.Line(version)));

    [Theory]
    [InlineData("18.11.0", "18.11.9", true)]
    [InlineData("18.12.0", "18.11.9", false)]
    [InlineData("1.20", "1.19.1", false)]
    [InlineData("16.2", "16.4.0", false)]
    [InlineData("6.3.3", "6.3.4", true)]
    public void A_version_lies_in_the_line_of_another(string version, string of, bool inLine) =>
        Assert.Equal(inLine, VersionLines.InLine(version, VersionLines.Line(of)));

    [Fact]
    public void Oversized_parts_end_the_leading_numbers_instead_of_throwing()
    {
        Assert.Equal([1L], VersionLines.LeadingNumbers("1.99999999999999999999.3"));
    }
}
