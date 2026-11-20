using Ardea.Assessment.Assessing;
using Ardea.Assessment.Matching;
using Ardea.Assets.Contracts;
using Ardea.Intel.Contracts;

namespace Ardea.Assessment.Tests;

public sealed class CoarsePathRuleTests
{
    [Theory]
    [InlineData("none", null, true, new long[0])]
    [InlineData("line", "17", true, new long[] { 17 })]
    [InlineData("line", "8.3", true, new long[] { 8, 3 })]
    [InlineData("exact", "17.4", false, new long[0])]
    public void Only_a_component_without_an_exact_version_goes_the_coarse_path_and_only_a_line_narrows(string precision, string? version, bool coarse, long[] line)
    {
        var component = Component(precision, version);

        Assert.Equal(coarse, NoticeRules.IsCoarse(component));
        Assert.Equal(line, NoticeRules.Line(component));
    }

    [Theory]
    [InlineData(false, "none", "none", false, null)]
    [InlineData(false, "active", "none", false, "exploited")]
    [InlineData(false, "active", "poc", false, "exploited")]
    [InlineData(false, "active", null, false, "exploited")]
    [InlineData(false, "active", "active", false, null)]
    [InlineData(true, "active", "active", false, "kev")]
    [InlineData(true, "active", "none", false, "kev")]
    [InlineData(true, "active", "active", true, null)]
    [InlineData(false, "poc", "none", false, null)]
    public void An_answered_notice_comes_back_when_kev_lists_the_entry_or_it_became_exploited_after_the_answer(
        bool inKev, string exploitation, string? answeredExploitation, bool answeredInKev, string? reason) =>
        Assert.Equal(reason, NoticeRules.ReappearReason(Entry(exploitation, inKev), answeredExploitation, answeredInKev));

    [Theory]
    [InlineData(null, "17.4", null, null, 17L, true)]
    [InlineData(null, "17.0", null, null, 17L, false)]
    [InlineData(null, null, "17.0", null, 17L, true)]
    [InlineData(null, null, "16.9", null, 17L, false)]
    [InlineData("17.4", null, null, null, 17L, true)]
    [InlineData("v16.0", "v16.4", null, null, 17L, false)]
    [InlineData("v17.0", "v17.4", null, null, 17L, true)]
    [InlineData(null, null, null, "17.*", 17L, true)]
    [InlineData(null, null, null, "16.*", 17L, false)]
    [InlineData(null, null, null, "unspecified", 17L, null)]
    [InlineData(null, null, null, null, 17L, null)]
    public void Open_ranges_and_wildcards_touch_a_line_only_where_their_bounds_reach_it(string? introduced, string? fixedAt, string? lastAffected, string? exact, long line, bool? touches) =>
        Assert.Equal(touches, LineNarrowing.Touches(new RangeView(introduced, fixedAt, lastAffected, exact, "affected"), [line]));

    [Theory]
    [InlineData("8.2.0", "8.3.0", false)]
    [InlineData("8.2.0", "8.3.1", true)]
    [InlineData("8.4.0", null, false)]
    [InlineData("8.0.0", null, true)]
    public void A_line_of_two_numbers_starts_at_its_first_release(string introduced, string? fixedAt, bool touches) =>
        Assert.Equal(touches, LineNarrowing.Touches(new RangeView(introduced, fixedAt, null, null, "affected"), [8, 3]));

    [Theory]
    [InlineData("1da177e4c3f4", "4f2e1a9b7c3d")]
    [InlineData("0", "5d0c6a7f21b3e4a8c9d0e1f2a3b4c5d6e7f8a9b0")]
    [InlineData(null, "3e1b9c4")]
    public void A_range_of_git_commits_stays_whatever_digits_its_hashes_start_with(string? introduced, string fixedAt)
    {
        var commits = new RangeView(introduced, fixedAt, null, null, "affected");

        Assert.Null(LineNarrowing.Touches(commits, [17]));
        Assert.True(LineNarrowing.Keeps([Statement(commits)], [17]));
        Assert.Null(LineNarrowing.Touches(new RangeView(null, null, null, "4f2e1a9", "affected"), [17]));
    }

    [Fact]
    public void Ranges_from_every_source_count_and_one_inside_the_line_keeps_the_entry()
    {
        var outside = Statement(new RangeView("16.0", "16.11", null, null, "affected"));
        var inside = Statement(new RangeView("17.0", "17.7", null, null, "affected"));

        Assert.False(LineNarrowing.Keeps([outside], [17]));
        Assert.True(LineNarrowing.Keeps([outside, inside], [17]));
        Assert.True(LineNarrowing.Keeps([outside, Statement(new RangeView("abc", "def", null, null, "affected"))], [17]));
        Assert.False(LineNarrowing.Keeps([outside, Statement(new RangeView("17.0", null, null, null, "unknown"))], [17]));
        Assert.True(LineNarrowing.Keeps([outside], []));
    }

    private static VersionStatement Statement(params RangeView[] ranges) => new(Guid.Empty, null, "unknown", "no_installed_version", "unknown", ranges);

    private static ProfileComponent Component(string precision, string? version) => new()
    {
        Id = Guid.CreateVersion7(),
        ProjectId = Guid.CreateVersion7(),
        ProjectArchived = false,
        ProjectName = "Stack",
        Name = "PostgreSQL",
        Kind = "database",
        ProductKey = "postgresql",
        Version = version,
        VersionPrecision = precision,
        Packaging = "vendor",
        Exposure = "internal",
        Criticality = "normal",
        Protection = "none",
        Origin = "manual",
    };

    private static EntryFacts Entry(string exploitation, bool inKev) => new()
    {
        Id = Guid.CreateVersion7(),
        Key = "CVE-2026-1",
        Kind = "vulnerability",
        State = "published",
        Revision = 1,
        ChangedAt = DateTimeOffset.UnixEpoch,
        Exploitation = exploitation,
        InKev = inKev,
        AuthorityWarning = false,
        FixAvailable = false,
        Affected = [],
    };
}
