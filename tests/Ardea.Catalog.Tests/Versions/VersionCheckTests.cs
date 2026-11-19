using Ardea.Catalog.Contracts;
using Ardea.Catalog.Versions;

namespace Ardea.Catalog.Tests.Versions;

/// <summary>The verdict table of master plan 6.6, row by row, and the cases that must stay «unknown».</summary>
public sealed class VersionCheckTests
{
    private static readonly IVersionScheme Numeric = VersionSchemeRegistry.Instance.Find("numeric")!;

    private static readonly VersionRange Affected24 = new() { Introduced = "2.4.0", Fixed = "2.4.51" };

    private static VersionCheck Check(string? installed, VersionStatus defaultStatus, params VersionRange[] ranges) =>
        VersionCheck.Evaluate(Numeric, installed, ranges, defaultStatus);

    private static VersionCheck Check(string? installed, VersionStatus defaultStatus, string[] fixesInText, params VersionRange[] ranges) =>
        VersionCheck.Evaluate(Numeric, installed, ranges, defaultStatus, fixesInText);

    [Fact]
    public void Row_1_inside_an_affected_range_is_in_range()
    {
        Assert.Equal(new VersionCheck(VersionVerdict.InRange, VersionCheckReason.InAffectedRange), Check("2.4.49", VersionStatus.Unaffected, Affected24));
    }

    [Fact]
    public void Row_1_wins_over_an_unaffected_range_that_also_contains_the_version()
    {
        var unaffected = new VersionRange { Introduced = "2.4.49", LastAffected = "2.4.49", Status = VersionStatus.Unaffected };

        Assert.Equal(VersionVerdict.InRange, Check("2.4.49", VersionStatus.Unknown, unaffected, Affected24).Verdict);
    }

    [Fact]
    public void Row_2_inside_an_unaffected_range_is_out_of_range()
    {
        // Linux kernel style: affected by default, the stable branches with the fix are unaffected.
        var backport = new VersionRange { Introduced = "5.10.200", Fixed = "5.11", Status = VersionStatus.Unaffected };

        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.InUnaffectedRange), Check("5.10.201", VersionStatus.Affected, backport));
    }

    [Fact]
    public void Row_3_in_no_range_with_default_affected_is_in_range()
    {
        var backport = new VersionRange { Introduced = "5.10.200", Fixed = "5.11", Status = VersionStatus.Unaffected };

        Assert.Equal(new VersionCheck(VersionVerdict.InRange, VersionCheckReason.DefaultAffected), Check("5.10.100", VersionStatus.Affected, backport));
    }

    [Fact]
    public void Row_4_in_no_range_with_default_unaffected_is_out_of_range()
    {
        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.DefaultUnaffected), Check("2.4.51", VersionStatus.Unaffected, Affected24));
    }

    [Theory]
    [InlineData("2.4.51")]
    [InlineData("2.4.58")]
    [InlineData("3.0")]
    public void Row_5_at_or_above_every_fix_without_default_is_out_of_range(string installed)
    {
        var older = new VersionRange { Introduced = "2.2.0", Fixed = "2.2.34" };

        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.AboveAllRanges), Check(installed, VersionStatus.Unknown, older, Affected24));
    }

    [Fact]
    public void Row_5_counts_last_affected_and_exact_versions_as_upper_bounds()
    {
        var upTo = new VersionRange { LastAffected = "1.4.2" };
        var single = VersionRange.ExactVersion("1.5.0");

        Assert.Equal(VersionVerdict.OutOfRange, Check("1.5.1", VersionStatus.Unknown, upTo, single).Verdict);
        Assert.Equal(VersionVerdict.InRange, Check("1.4.2", VersionStatus.Unknown, upTo, single).Verdict);
    }

    [Fact]
    public void Row_5_does_not_apply_while_one_range_is_open_upwards()
    {
        var open = new VersionRange { Introduced = "3.0" };

        Assert.Equal(VersionVerdict.InRange, Check("3.5", VersionStatus.Unknown, Affected24, open).Verdict);
    }

    [Fact]
    public void Below_all_ranges_with_an_explicit_lower_bound_is_out_of_range()
    {
        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.OutsideNamedBounds), Check("2.2.9", VersionStatus.Unknown, Affected24));
    }

    [Fact]
    public void Between_two_ranges_with_explicit_bounds_is_out_of_range()
    {
        var older = new VersionRange { Introduced = "2.2.0", Fixed = "2.2.34" };

        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.OutsideNamedBounds), Check("2.3.1", VersionStatus.Unknown, older, Affected24));
    }

    [Fact]
    public void Below_a_range_without_named_lower_bound_cannot_happen_it_is_inside()
    {
        // «< 2.4.51» names no lower bound: every older version is affected.
        var below = new VersionRange { Fixed = "2.4.51" };

        Assert.Equal(VersionVerdict.InRange, Check("1.3", VersionStatus.Unknown, below).Verdict);
    }

    [Fact]
    public void Row_6_otherwise_unknown()
    {
        // Only unaffected ranges and no default: the record does not say whether 1.0 is affected.
        var unaffected = new VersionRange { Introduced = "2.0", Status = VersionStatus.Unaffected };

        Assert.Equal(new VersionCheck(VersionVerdict.Unknown, VersionCheckReason.NotInAnyRange), Check("1.0", VersionStatus.Unknown, unaffected));
    }

    [Fact]
    public void Inside_a_range_of_unknown_status_is_unknown()
    {
        var unclear = new VersionRange { Introduced = "2.0", Fixed = "3.0", Status = VersionStatus.Unknown };

        Assert.Equal(new VersionCheck(VersionVerdict.Unknown, VersionCheckReason.InUnknownRange), Check("2.5", VersionStatus.Unaffected, unclear));
    }

    [Theory]
    [InlineData(null, VersionCheckReason.NoInstalledVersion)]
    [InlineData("", VersionCheckReason.NoInstalledVersion)]
    [InlineData("latest", VersionCheckReason.InstalledVersionNotReadable)]
    public void Missing_or_unreadable_installed_version_is_unknown_never_out_of_range(string? installed, VersionCheckReason reason)
    {
        // Even a default of «unaffected» does not turn an unknown version into «not affected».
        Assert.Equal(new VersionCheck(VersionVerdict.Unknown, reason), Check(installed, VersionStatus.Unaffected, Affected24));
    }

    [Fact]
    public void Unreadable_affected_range_makes_every_later_row_unknown()
    {
        var unreadable = new VersionRange { Introduced = "before the rewrite", Fixed = "2.0" };

        Assert.Equal(new VersionCheck(VersionVerdict.Unknown, VersionCheckReason.RangeNotReadable), Check("1.5", VersionStatus.Unaffected, unreadable));
    }

    [Fact]
    public void Unreadable_lower_bound_does_not_matter_at_or_above_a_readable_fix()
    {
        // Microsoft, System.Text.Json (CVE-2026-120796): {"version": "NOT_YET_AVAILABLE", "lessThan": "11.0.0"}.
        var range = new VersionRange { Introduced = "NOT_YET_AVAILABLE", Fixed = "11.0.0" };
        var upTo = new VersionRange { Introduced = "NOT_YET_AVAILABLE", LastAffected = "10.0.4" };

        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.AboveAllRanges), Check("11.0.1", VersionStatus.Unknown, range));
        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.AboveAllRanges), Check("11.0.0", VersionStatus.Unknown, range));
        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.AboveAllRanges), Check("10.0.5", VersionStatus.Unknown, upTo));

        // Below the fix the unreadable lower bound decides whether the version is inside: unknown.
        Assert.Equal(new VersionCheck(VersionVerdict.Unknown, VersionCheckReason.RangeNotReadable), Check("10.0.9", VersionStatus.Unknown, range));
    }

    [Fact]
    public void Readable_affected_range_still_decides_next_to_an_unreadable_one()
    {
        var unreadable = VersionRange.ExactVersion("build 4711 of the old line");

        Assert.Equal(VersionVerdict.InRange, Check("2.4.50", VersionStatus.Unknown, unreadable, Affected24).Verdict);
    }

    [Fact]
    public void Git_commits_are_stored_but_not_evaluated()
    {
        var commits = new VersionRange { Introduced = "1da177e4c3f4", Fixed = "a1b2c3d4e5f6", Scheme = VersionSchemeNames.Git };

        Assert.Equal(new VersionCheck(VersionVerdict.Unknown, VersionCheckReason.NoComparableRange), Check("6.1", VersionStatus.Unknown, commits));
    }

    [Fact]
    public void Version_numbers_next_to_git_commits_count()
    {
        // Linux kernel records carry both; the version ranges decide.
        var commits = new VersionRange { Introduced = "1da177e4c3f4", Fixed = "a1b2c3d4e5f6", Scheme = VersionSchemeNames.Git };
        var versions = new VersionRange { Introduced = "5.15", Fixed = "5.15.140", Scheme = "semver" };

        Assert.Equal(VersionVerdict.InRange, Check("5.15.100", VersionStatus.Unknown, commits, versions).Verdict);
        Assert.Equal(VersionVerdict.OutOfRange, Check("5.15.140", VersionStatus.Unknown, commits, versions).Verdict);
    }

    [Fact]
    public void Default_status_is_not_applied_when_the_record_names_only_git_commits()
    {
        // Linux kernel records: one entry with commit ranges and default «unaffected». Any version may lie inside a
        // commit range, so «in no range, default unaffected» cannot be concluded; the version entry decides instead.
        var commits = new VersionRange { Introduced = "db6fbc106786f26d95889c50c18b1f28aa543a17", Fixed = "253cff0526781e28b1b0f9fdacd74ecdc23d3ab7", Scheme = VersionSchemeNames.Git };
        var fixedCommit = new VersionRange { Exact = "8df4bcdb0a4232192b2445256c39b787d58ef14d", Scheme = VersionSchemeNames.Git };

        Assert.Equal(new VersionCheck(VersionVerdict.Unknown, VersionCheckReason.NoComparableRange), Check("6.12.1", VersionStatus.Unaffected, commits, fixedCommit));
    }

    [Fact]
    public void An_affected_default_still_holds_next_to_commit_ranges_only()
    {
        // Every version is affected: by default, or inside an affected commit range. Only an unaffected commit range leaves doubt.
        var commits = new VersionRange { Introduced = "abcdef1", Fixed = "1234abc", Scheme = VersionSchemeNames.Git };
        var fixedCommit = new VersionRange { Introduced = "1234abc", Scheme = VersionSchemeNames.Git, Status = VersionStatus.Unaffected };

        Assert.Equal(new VersionCheck(VersionVerdict.InRange, VersionCheckReason.DefaultAffected), Check("1.0", VersionStatus.Affected, commits));
        Assert.Equal(new VersionCheck(VersionVerdict.Unknown, VersionCheckReason.NoComparableRange), Check("1.0", VersionStatus.Affected, commits, fixedCommit));
        Assert.Equal(new VersionCheck(VersionVerdict.Unknown, VersionCheckReason.NoComparableRange), Check("1.0", VersionStatus.Unknown, commits));
    }

    [Fact]
    public void Version_numbers_tagged_git_are_compared_as_versions()
    {
        // Symfony (CVE-2026-118515): {"version": "0", "lessThan": "5.4.56", "versionType": "git"}, stored without the open lower bound.
        var tagged = new VersionRange { Fixed = "5.4.56", Scheme = VersionSchemeNames.Git };
        var line = new VersionRange { Introduced = "6.0.0", Fixed = "6.4.50", Scheme = VersionSchemeNames.Git };

        Assert.False(tagged.IsCommitRange);
        Assert.Equal(new VersionCheck(VersionVerdict.InRange, VersionCheckReason.InAffectedRange), Check("1.0.0", VersionStatus.Unaffected, tagged, line));
        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.DefaultUnaffected), Check("6.4.50", VersionStatus.Unaffected, tagged, line));
    }

    [Theory]
    [InlineData("1da177e4c3f4", null, true)]
    [InlineData("0", "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2", true)]
    [InlineData(null, null, true)]
    [InlineData("0", null, true)]
    [InlineData("6.0.0", "6.4.50", false)]
    [InlineData(null, "5.4.56", false)]
    public void A_git_range_is_a_commit_range_when_a_bound_is_a_hash_or_none_is_named(string? introduced, string? fixedVersion, bool commits)
    {
        Assert.Equal(commits, new VersionRange { Introduced = introduced, Fixed = fixedVersion, Scheme = "git" }.IsCommitRange);
        Assert.False(new VersionRange { Introduced = introduced, Fixed = fixedVersion, Scheme = "semver" }.IsCommitRange);
    }

    [Fact]
    public void Fix_named_in_text_above_the_version_keeps_row_4_unknown()
    {
        // Apache DataSketches (CVE-2026-122096): «through 9.0.0», default unaffected; «upgrade to version 9.1.0, which fixes the issue».
        var through = new VersionRange { LastAffected = "9.0.0" };

        Assert.Equal(new VersionCheck(VersionVerdict.Unknown, VersionCheckReason.FixNamedInTextAboveInstalled), Check("9.0.1", VersionStatus.Unaffected, ["9.1.0"], through));
        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.DefaultUnaffected), Check("9.1.0", VersionStatus.Unaffected, ["9.1.0"], through));
        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.DefaultUnaffected), Check("9.0.1", VersionStatus.Unaffected, [], through));
    }

    [Fact]
    public void Fix_named_in_text_above_the_version_keeps_row_5_unknown()
    {
        // Apache TomEE (CVE-2026-111245): openejb 4.0.0-beta-1 through 4.7.5, no fix in that line; «upgrade to 10.3.0 or 11.0.0-M2».
        var old = new VersionRange { Introduced = "4.0.0", LastAffected = "4.7.5" };

        Assert.Equal(new VersionCheck(VersionVerdict.Unknown, VersionCheckReason.FixNamedInTextAboveInstalled), Check("4.7.6", VersionStatus.Unknown, ["10.3.0", "11.0.0"], old));
    }

    [Fact]
    public void A_fix_of_the_versions_own_line_at_or_below_it_lets_the_ranges_stand()
    {
        // ActiveMQ (CVE-2026-108500): 6.3.0 before 6.3.3 and 5.19.0 before 5.19.12; «upgrade to 6.4.0, 6.3.3 or 5.19.12».
        VersionRange[] ranges = [new() { Introduced = "6.3.0", Fixed = "6.3.3" }, new() { Introduced = "5.19.0", Fixed = "5.19.12" }];
        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.DefaultUnaffected), Check("6.3.4", VersionStatus.Unaffected, ["6.4.0", "6.3.3", "5.19.12"], ranges));

        // n8n (CVE-2026-126388): «before 2.46.1» in the text, but the ranges fix the 2.45 line with 2.45.5 (an unaffected version).
        VersionRange[] backport = [new() { Fixed = "2.45.5" }, VersionRange.ExactVersion("2.45.5", VersionStatus.Unaffected)];
        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.DefaultUnaffected), Check("2.45.6", VersionStatus.Unaffected, ["2.46.1"], backport));

        // The same text fix in another line (1.20 is not the line 1.19) does not count as the version's own.
        Assert.Equal(VersionVerdict.Unknown, Check("1.19.1", VersionStatus.Unaffected, ["1.20"], new VersionRange { LastAffected = "1.19.0" }).Verdict);
    }

    [Fact]
    public void Text_does_not_change_a_version_between_ranges_or_inside_an_unaffected_range()
    {
        // Between two lines the later line's fix is above the version, but the version is not above every range.
        VersionRange[] lines = [new() { Introduced = "1.0", Fixed = "1.5.3" }, new() { Introduced = "2.0", Fixed = "2.1.4" }];
        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.OutsideNamedBounds), Check("1.6", VersionStatus.Unknown, ["1.5.3", "2.1.4"], lines));

        // An explicit unaffected statement is more specific than any prose.
        var backport = new VersionRange { Introduced = "5.10.200", Fixed = "5.11", Status = VersionStatus.Unaffected };
        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.InUnaffectedRange), Check("5.10.201", VersionStatus.Affected, ["6.0"], backport));
    }

    [Theory]
    [InlineData("16.4")]
    [InlineData("16.4.0")]
    [InlineData("16.4.0.0")]
    public void The_line_guard_does_not_depend_on_how_many_parts_the_version_is_written_with(string installed)
    {
        // The fix 16.2 belongs to the line 16.2, not to 16.4: the text's later fix 16.6 keeps every spelling unknown.
        var range = new VersionRange { Introduced = "16.0", Fixed = "16.2" };

        Assert.Equal(new VersionCheck(VersionVerdict.Unknown, VersionCheckReason.FixNamedInTextAboveInstalled), Check(installed, VersionStatus.Unknown, ["16.6"], range));
    }

    [Fact]
    public void Text_versions_the_scheme_cannot_read_are_ignored()
    {
        Assert.Equal(VersionVerdict.OutOfRange, Check("2.4.52", VersionStatus.Unaffected, ["build 4711"], Affected24).Verdict);
    }

    [Theory]
    // Google writes «Chrome prior to 157.0.8103.50» as an empty range: below it, it reads as «before the fix».
    [InlineData("157.0.8103.50", "157.0.8103.50", "157.0.8102.50", VersionVerdict.InRange, VersionCheckReason.BeforeEmptyRange)]
    [InlineData("157.0.8103.50", "157.0.8103.50", "157.0.8103.50", VersionVerdict.OutOfRange, VersionCheckReason.AboveAllRanges)]
    // An empty range never serves as an explicit lower bound, also when it starts a line or differs only in writing.
    [InlineData("2.0.0", "2.0.0", "1.9.0", VersionVerdict.InRange, VersionCheckReason.BeforeEmptyRange)]
    [InlineData("v2.0.1", "2.0.1", "1.9", VersionVerdict.InRange, VersionCheckReason.BeforeEmptyRange)]
    [InlineData("18.12.0", "18.12.0", "18.11.9", VersionVerdict.InRange, VersionCheckReason.BeforeEmptyRange)]
    public void An_empty_range_reads_as_before_its_fix(string introduced, string fixedVersion, string installed, VersionVerdict verdict, VersionCheckReason reason)
    {
        var empty = new VersionRange { Introduced = introduced, Fixed = fixedVersion };

        Assert.Equal(new VersionCheck(verdict, reason), Check(installed, VersionStatus.Unknown, empty));
    }

    [Fact]
    public void An_explicit_unaffected_range_wins_over_the_reading_of_an_empty_range()
    {
        var empty = new VersionRange { Introduced = "2.0.0", Fixed = "2.0.0" };
        var unaffected = new VersionRange { Introduced = "1.9.0", Fixed = "2.0.0", Status = VersionStatus.Unaffected };

        Assert.Equal(new VersionCheck(VersionVerdict.OutOfRange, VersionCheckReason.InUnaffectedRange), Check("1.9.5", VersionStatus.Unknown, empty, unaffected));
    }

    [Fact]
    public void Without_a_scheme_the_verdict_is_unknown()
    {
        Assert.Equal(new VersionCheck(VersionVerdict.Unknown, VersionCheckReason.NoScheme), VersionCheck.Evaluate(null, "1.0", [Affected24], VersionStatus.Unaffected));
    }
}
