using Ardea.Catalog.Contracts;
using Ardea.Catalog.Versions;

namespace Ardea.Catalog.Tests.Versions;

/// <summary>Known peculiarities of version statements, resolved while normalising (master plan 6.6).</summary>
public sealed class VersionRangesTests
{
    [Theory]
    [InlineData("8.2.*", "8.2", "8.3")]
    [InlineData("8.x", "8", "9")]
    [InlineData("5.4.X", "5.4", "5.5")]
    [InlineData("1.9.*", "1.9", "1.10")]
    public void Placeholder_becomes_a_range_up_to_the_next_line(string text, string lower, string upper)
    {
        Assert.True(VersionRanges.TryExpandWildcard(text, out var from, out var to));
        Assert.Equal((lower, upper), (from, to));
    }

    [Theory]
    // A lower bound in the placeholder's line leaves only «to the end of that line»; read as «before the line» it would be empty.
    [InlineData("6.12.0", "6.12.*", VersionStatus.Affected, "6.12.0", "6.13")]
    [InlineData("5.10", "5.10.*", VersionStatus.Affected, "5.10", "5.11")]
    [InlineData("2.0", "2.x", VersionStatus.Affected, "2.0", "3")]
    [InlineData("6.12.*", "6.12.*", VersionStatus.Affected, "6.12", "6.13")]
    [InlineData("6.12.116", "6.12.*", VersionStatus.Unaffected, "6.12.116", "6.13")]
    // Below the line or without a lower bound: the wider reading when affected, the narrower when unaffected.
    [InlineData("4.0", "6.12.*", VersionStatus.Affected, "4.0", "6.13")]
    [InlineData("4.0", "6.12.*", VersionStatus.Unaffected, "4.0", "6.12")]
    [InlineData("0", "6.12.*", VersionStatus.Affected, null, "6.13")]
    [InlineData("0", "6.12.*", VersionStatus.Unaffected, null, "6.12")]
    // A lower bound above the line gives an inverted range; one without numbers cannot be placed and stays unreadable.
    [InlineData("6.13.0", "6.12.*", VersionStatus.Affected, "6.13.0", "6.12")]
    [InlineData("NOT_YET_AVAILABLE", "6.12.*", VersionStatus.Affected, "NOT_YET_AVAILABLE", "6.12.*")]
    public void Less_than_a_placeholder_ends_where_the_lower_bound_allows(string introduced, string lessThan, VersionStatus status, string? expectedIntroduced, string expectedFixed)
    {
        var range = VersionRanges.Normalize(new VersionRange { Introduced = introduced, Fixed = lessThan, Status = status });

        Assert.Equal((expectedIntroduced, expectedFixed), (range.Introduced, range.Fixed));
        Assert.Equal(range, VersionRanges.Normalize(range));
    }

    [Theory]
    [InlineData("6.12.0", "6.12.*", "6.12.3", VersionVerdict.InRange)]
    [InlineData("5.10", "5.10.*", "5.10.200", VersionVerdict.InRange)]
    [InlineData("2.0", "2.x", "2.5", VersionVerdict.InRange)]
    [InlineData("NOT_YET_AVAILABLE", "6.12.*", "6.12.3", VersionVerdict.Unknown)]
    public void Less_than_a_placeholder_is_never_a_wrong_out_of_range(string introduced, string lessThan, string installed, VersionVerdict verdict)
    {
        var range = VersionRanges.Normalize(new VersionRange { Introduced = introduced, Fixed = lessThan });

        Assert.Equal(verdict, VersionCheck.Evaluate(VersionSchemeRegistry.Instance.Find("numeric"), installed, [range], VersionStatus.Unknown).Verdict);
    }

    [Fact]
    public void A_placeholder_with_an_oversized_part_is_no_placeholder_and_does_not_throw()
    {
        // A part beyond 18 digits overflowed long.Parse inside the correlation, which then failed instead of degrading.
        Assert.False(VersionRanges.TryExpandWildcard("99999999999999999999.*", out _, out _));
        var range = VersionRanges.Normalize(new VersionRange { Introduced = "1.0", LastAffected = "99999999999999999999.*" });
        Assert.Equal((null, null), (range.Fixed, range.LastAffected));
    }

    [Theory]
    [InlineData("8.4.0-8.4.13", "8.4.0", "8.4.13")]
    [InlineData("8.4.0 - 8.4.13", "8.4.0", "8.4.13")]
    [InlineData("2.0 to 2.3", "2.0", "2.3")]
    public void Range_in_one_field_is_split(string text, string from, string to)
    {
        Assert.True(VersionRanges.TrySplitRange(text, out var a, out var b));
        Assert.Equal((from, to), (a, b));
    }

    [Theory]
    [InlineData("1.0.0-rc.1")]
    [InlineData("1.0.0-1.0.5.1")]
    [InlineData("1.2-4")]
    [InlineData("8.4.13-8.4.0")]
    [InlineData("2.4.58")]
    public void Pre_releases_releases_and_reversed_ends_are_not_split(string text) =>
        Assert.False(VersionRanges.TrySplitRange(text, out _, out _));

    [Theory]
    [InlineData("0", true)]
    [InlineData("*", true)]
    [InlineData("unspecified", true)]
    [InlineData(null, true)]
    [InlineData("0.1", false)]
    public void Zero_and_star_are_open_lower_bounds(string? text, bool open) => Assert.Equal(open, VersionRanges.IsOpenLowerBound(text));

    [Fact]
    public void Cve_version_with_less_than_is_a_half_open_range()
    {
        var range = VersionRanges.FromCve("2.4.0", "2.4.51", null, VersionStatus.Affected, "custom");

        Assert.Equal(new VersionRange { Introduced = "2.4.0", Fixed = "2.4.51", Scheme = "custom" }, range);
    }

    [Fact]
    public void Cve_open_bounds_zero_to_star_cover_everything()
    {
        var range = VersionRanges.FromCve("0", "*", null, VersionStatus.Affected);

        Assert.Equal(new VersionRange(), range);
        Assert.False(range.HasExplicitLowerBound);
    }

    [Fact]
    public void Cve_less_than_or_equal_to_a_placeholder_ends_before_the_next_line()
    {
        // Linux kernel: {"version": "5.10.200", "lessThanOrEqual": "5.10.*", "status": "unaffected"}.
        var range = VersionRanges.FromCve("5.10.200", null, "5.10.*", VersionStatus.Unaffected, "semver");

        Assert.Equal(new VersionRange { Introduced = "5.10.200", Fixed = "5.11", Status = VersionStatus.Unaffected, Scheme = "semver" }, range);
    }

    [Theory]
    [InlineData("8.2.*", "8.2", "8.3", null, null)]
    [InlineData("8.4.0-8.4.13", "8.4.0", null, "8.4.13", null)]
    [InlineData("< 2.1", null, "2.1", null, null)]
    [InlineData("<= 2.1", null, null, "2.1", null)]
    [InlineData(">= 2.1", "2.1", null, null, null)]
    [InlineData("2.4.49", null, null, null, "2.4.49")]
    public void Cve_single_version_field_is_resolved(string version, string? introduced, string? fixedVersion, string? lastAffected, string? exact)
    {
        var range = VersionRanges.FromCve(version, null, null, VersionStatus.Affected);

        Assert.Equal(new VersionRange { Introduced = introduced, Fixed = fixedVersion, LastAffected = lastAffected, Exact = exact }, range);
    }

    [Fact]
    public void Rpm_release_in_a_single_field_is_not_split()
    {
        var range = VersionRanges.FromCve("3.0.7-27.el9", null, null, VersionStatus.Affected, "rpm");

        Assert.Equal("3.0.7-27.el9", range.Exact);
    }

    [Fact]
    public void Osv_events_open_and_close_ranges_in_order()
    {
        // GHSA-jfh8-c2jp-5v3q (log4j-core): two branches in one range.
        KeyValuePair<string, string>[] events =
        [
            new("introduced", "2.0-beta9"), new("fixed", "2.3.1"),
            new("introduced", "2.4"), new("fixed", "2.12.2"),
            new("introduced", "2.13.0"), new("fixed", "2.16.0"),
        ];

        var ranges = VersionRanges.FromOsvEvents(events);

        Assert.Equal(
            [
                new VersionRange { Introduced = "2.0-beta9", Fixed = "2.3.1" },
                new VersionRange { Introduced = "2.4", Fixed = "2.12.2" },
                new VersionRange { Introduced = "2.13.0", Fixed = "2.16.0" },
            ],
            ranges);
    }

    [Fact]
    public void Osv_introduced_zero_has_no_lower_bound_and_open_end_stays_open()
    {
        var ranges = VersionRanges.FromOsvEvents([new("introduced", "0"), new("last_affected", "1.4.2"), new("introduced", "2.0"), new("limit", "3.0")]);

        Assert.Equal([new VersionRange { LastAffected = "1.4.2" }, new VersionRange { Introduced = "2.0" }], ranges);
    }

    [Theory]
    // Oracle: "Supported versions that are affected are MySQL Server: 8.4.0-8.4.13" (CVE-2026-114949).
    [InlineData(null, null, null, "8.4.0-8.4.13", "8.4.0", null, "8.4.13", null)]
    [InlineData(null, null, null, "8.2.*", "8.2", "8.3", null, null)]
    [InlineData(null, null, null, "< 2.1", null, "2.1", null, null)]
    [InlineData(null, null, null, "2.4.49", null, null, null, "2.4.49")]
    // An open word alone names no version; it is not read as «every version».
    [InlineData(null, null, null, "unspecified", null, null, null, "unspecified")]
    // Linux kernel: {"version": "6.12.116", "lessThanOrEqual": "6.12.*", "status": "unaffected"}.
    [InlineData("6.12.116", null, "6.12.*", null, "6.12.116", "6.13", null, null)]
    [InlineData("7.4-rc1", null, "*", null, "7.4-rc1", null, null, null)]
    [InlineData("0", "2.0", null, null, null, "2.0", null, null)]
    [InlineData("unspecified", "2.0", null, null, null, "2.0", null, null)]
    // Log4Shell: {"version": "2.0-beta9", "lessThan": "log4j-core*"}.
    [InlineData("2.15.0", "log4j-core*", null, null, "2.15.0", null, null, null)]
    [InlineData("1.0", null, "1.4.2", null, "1.0", null, "1.4.2", null)]
    public void Stored_range_is_normalised(
        string? introduced, string? fixedVersion, string? lastAffected, string? exact,
        string? expectedIntroduced, string? expectedFixed, string? expectedLastAffected, string? expectedExact)
    {
        var stored = new VersionRange { Introduced = introduced, Fixed = fixedVersion, LastAffected = lastAffected, Exact = exact, Scheme = "custom" };

        var range = VersionRanges.Normalize(stored);

        Assert.Equal(new VersionRange { Introduced = expectedIntroduced, Fixed = expectedFixed, LastAffected = expectedLastAffected, Exact = expectedExact, Scheme = "custom" }, range);
        Assert.Equal(range, VersionRanges.Normalize(range));
    }

    [Theory]
    // Google, «Chrome prior to 157.0.8103.50»; Microsoft, «Visual Studio 2026 version 18.12»; a plain one; one with a «v».
    [InlineData("157.0.8103.50", "157.0.8103.50")]
    [InlineData("18.12.0", "18.12.0")]
    [InlineData("v2.0.1", "2.0.1")]
    public void A_range_whose_lower_bound_equals_its_fix_is_stored_as_written(string introduced, string fixedVersion)
    {
        var stored = new VersionRange { Introduced = introduced, Fixed = fixedVersion, Scheme = "custom" };

        Assert.Equal(stored, VersionRanges.Normalize(stored));
    }

    [Fact]
    public void Lower_bound_below_the_fix_stays()
    {
        var range = VersionRanges.Normalize(new VersionRange { Introduced = "157.0", Fixed = "157.0.8103.50" });

        Assert.Equal("157.0", range.Introduced);
    }

    [Fact]
    public void Normalising_keeps_status_and_does_not_split_rpm_releases()
    {
        var rpm = VersionRanges.Normalize(new VersionRange { Exact = "3.0.7-27.el9", Status = VersionStatus.Unaffected, Scheme = "rpm" });

        Assert.Equal(new VersionRange { Exact = "3.0.7-27.el9", Status = VersionStatus.Unaffected, Scheme = "rpm" }, rpm);
    }

    [Fact]
    public void Kernel_stable_line_after_normalising_no_longer_swallows_later_lines()
    {
        // CVE-2026-100082 as stored: affected by default from 4.5; each stable line unaffected from its fix to the end of that line.
        // Before normalising, «5.10.274 to 5.10.*» read as «5.10.274 and everything above», which contained 6.12.1.
        VersionRange[] stored =
        [
            VersionRange.ExactVersion("4.5"),
            new() { Introduced = "0", Fixed = "4.5", Status = VersionStatus.Unaffected, Scheme = "semver" },
            new() { Introduced = "5.10.274", LastAffected = "5.10.*", Status = VersionStatus.Unaffected, Scheme = "semver" },
            new() { Introduced = "6.12.116", LastAffected = "6.12.*", Status = VersionStatus.Unaffected, Scheme = "semver" },
            new() { Introduced = "7.4-rc2", LastAffected = "*", Status = VersionStatus.Unaffected, Scheme = "original_commit_for_fix" },
        ];
        var ranges = stored.Select(VersionRanges.Normalize).ToList();
        var scheme = VersionSchemeRegistry.Instance.Find("semver");

        Assert.Equal(new VersionCheck(VersionVerdict.InRange, VersionCheckReason.DefaultAffected), VersionCheck.Evaluate(scheme, "6.12.1", ranges, VersionStatus.Affected));
        Assert.Equal(VersionVerdict.OutOfRange, VersionCheck.Evaluate(scheme, "6.12.120", ranges, VersionStatus.Affected).Verdict);
        Assert.Equal(VersionVerdict.OutOfRange, VersionCheck.Evaluate(scheme, "5.10.280", ranges, VersionStatus.Affected).Verdict);
    }

    [Fact]
    public void Normalised_kernel_record_gives_the_expected_verdicts()
    {
        // The shape of Linux kernel CVE records: one affected version, unaffected below it and on fixed stable branches.
        VersionRange[] ranges =
        [
            VersionRanges.FromCve("5.4", null, null, VersionStatus.Affected, "semver"),
            VersionRanges.FromCve("0", "5.4", null, VersionStatus.Unaffected, "semver"),
            VersionRanges.FromCve("5.10.200", null, "5.10.*", VersionStatus.Unaffected, "semver"),
            VersionRanges.FromCve("6.6", null, "*", VersionStatus.Unaffected, "original_commit_for_fix"),
        ];
        var scheme = VersionSchemeRegistry.Instance.Find("semver");

        Assert.Equal(VersionVerdict.OutOfRange, VersionCheck.Evaluate(scheme, "4.19.300", ranges, VersionStatus.Affected).Verdict);
        Assert.Equal(VersionVerdict.InRange, VersionCheck.Evaluate(scheme, "5.10.100", ranges, VersionStatus.Affected).Verdict);
        Assert.Equal(VersionVerdict.OutOfRange, VersionCheck.Evaluate(scheme, "5.10.205", ranges, VersionStatus.Affected).Verdict);
        Assert.Equal(VersionVerdict.OutOfRange, VersionCheck.Evaluate(scheme, "6.8.1", ranges, VersionStatus.Affected).Verdict);
    }
}
