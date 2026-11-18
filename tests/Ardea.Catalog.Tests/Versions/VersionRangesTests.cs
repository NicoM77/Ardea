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
