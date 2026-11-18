namespace Ardea.Catalog.Contracts;

/// <summary>The three outcomes of the version check (master plan 6.6). Unknown never means «not affected».</summary>
public enum VersionVerdict
{
    Unknown,
    InRange,
    OutOfRange,
}

/// <summary>Which rule gave the verdict; the rows of the table in master plan 6.6 and the reasons for «unknown».</summary>
public enum VersionCheckReason
{
    /// <summary>Row 1: inside a range with status affected.</summary>
    InAffectedRange,

    /// <summary>Row 2: inside a range with status unaffected.</summary>
    InUnaffectedRange,

    /// <summary>Row 3: in no range, the record's default status is affected.</summary>
    DefaultAffected,

    /// <summary>Row 4: in no range, the record's default status is unaffected.</summary>
    DefaultUnaffected,

    /// <summary>Row 5: in no range, no default status, at or above the fix or upper bound of every affected range.</summary>
    AboveAllRanges,

    /// <summary>
    /// In no range, no default status, and for every affected range either above it or below a lower
    /// bound the record names explicitly (below all ranges, or in a gap between them).
    /// </summary>
    OutsideNamedBounds,

    /// <summary>Row 6: in no range and none of the rules above applies.</summary>
    NotInAnyRange,

    /// <summary>Inside a range whose status the record leaves unknown.</summary>
    InUnknownRange,

    /// <summary>The profile has no version.</summary>
    NoInstalledVersion,

    /// <summary>The installed version is not a version of the scheme.</summary>
    InstalledVersionNotReadable,

    /// <summary>A bound of a range is not a version of the scheme, so its position cannot be decided.</summary>
    RangeNotReadable,

    /// <summary>The record names no range that can be compared (none at all, or only git commits).</summary>
    NoComparableRange,

    /// <summary>No version scheme compares these versions (Debian, date-based, …).</summary>
    NoScheme,
}

/// <summary>The verdict with the rule that gave it.</summary>
public readonly record struct VersionCheck(VersionVerdict Verdict, VersionCheckReason Reason)
{
    /// <summary>
    /// Checks an installed version against the ranges of one affected product (master plan 6.6). The
    /// rows of the table apply in order; ranges of type <c>git</c> are skipped.
    /// </summary>
    /// <param name="scheme">The scheme to compare with; <c>null</c> gives <see cref="VersionCheckReason.NoScheme"/>.</param>
    /// <param name="installed">The version in the profile.</param>
    /// <param name="ranges">The product's ranges from one record.</param>
    /// <param name="defaultStatus">The record's status for every version it does not name; <see cref="VersionStatus.Unknown"/> when it names none.</param>
    public static VersionCheck Evaluate(IVersionScheme? scheme, string? installed, IReadOnlyCollection<VersionRange> ranges, VersionStatus defaultStatus)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        if (string.IsNullOrWhiteSpace(installed))
        {
            return Unknown(VersionCheckReason.NoInstalledVersion);
        }

        if (scheme is null)
        {
            return Unknown(VersionCheckReason.NoScheme);
        }

        if (!scheme.TryParse(installed.Trim(), out var version))
        {
            return Unknown(VersionCheckReason.InstalledVersionNotReadable);
        }

        var positions = ranges
            .Where(r => !string.Equals(r.Scheme, VersionSchemeNames.Git, StringComparison.OrdinalIgnoreCase))
            .Select(r => (Range: r, Position: Locate(scheme, version, r)))
            .ToList();

        // Row 1 first: a readable affected range that contains the version decides whatever else the record says.
        if (positions.Exists(p => p.Position == Position.Inside && p.Range.Status == VersionStatus.Affected))
        {
            return new(VersionVerdict.InRange, VersionCheckReason.InAffectedRange);
        }

        // Every further row needs to know that no affected range contains the version.
        if (positions.Exists(p => p.Position == Position.Unreadable && p.Range.Status != VersionStatus.Unaffected))
        {
            return Unknown(VersionCheckReason.RangeNotReadable);
        }

        if (positions.Exists(p => p.Position == Position.Inside && p.Range.Status == VersionStatus.Unaffected))
        {
            return new(VersionVerdict.OutOfRange, VersionCheckReason.InUnaffectedRange);
        }

        if (positions.Exists(p => p.Position == Position.Inside))
        {
            return Unknown(VersionCheckReason.InUnknownRange);
        }

        // From here on «in no range» must hold, which an unreadable unaffected range leaves open.
        if (positions.Exists(p => p.Position == Position.Unreadable))
        {
            return Unknown(VersionCheckReason.RangeNotReadable);
        }

        switch (defaultStatus)
        {
            case VersionStatus.Affected:
                return new(VersionVerdict.InRange, VersionCheckReason.DefaultAffected);
            case VersionStatus.Unaffected:
                return new(VersionVerdict.OutOfRange, VersionCheckReason.DefaultUnaffected);
        }

        var affected = positions.Where(p => p.Range.Status == VersionStatus.Affected).ToList();
        if (affected.Count == 0)
        {
            return Unknown(positions.Count == 0 ? VersionCheckReason.NoComparableRange : VersionCheckReason.NotInAnyRange);
        }

        // Row 5, the deliberate interpretation: above every fix or upper bound.
        if (affected.TrueForAll(p => p.Position == Position.Above))
        {
            return new(VersionVerdict.OutOfRange, VersionCheckReason.AboveAllRanges);
        }

        // Below a range counts only when the record names that lower bound itself.
        if (affected.TrueForAll(p => p.Position is Position.Above or Position.BelowNamedBound))
        {
            return new(VersionVerdict.OutOfRange, VersionCheckReason.OutsideNamedBounds);
        }

        return Unknown(VersionCheckReason.NotInAnyRange);
    }

    private static VersionCheck Unknown(VersionCheckReason reason) => new(VersionVerdict.Unknown, reason);

    private static Position Locate(IVersionScheme scheme, ParsedVersion version, VersionRange range)
    {
        if (range.Exact is not null)
        {
            if (!scheme.TryParse(range.Exact, out var exact))
            {
                return Position.Unreadable;
            }

            var c = version.CompareTo(exact);
            return c == 0 ? Position.Inside : c > 0 ? Position.Above : Position.BelowNamedBound;
        }

        ParsedVersion? introduced = null, fixedVersion = null, lastAffected = null;
        if ((range.Introduced is not null && !scheme.TryParse(range.Introduced, out introduced))
            || (range.Fixed is not null && !scheme.TryParse(range.Fixed, out fixedVersion))
            || (range.LastAffected is not null && !scheme.TryParse(range.LastAffected, out lastAffected)))
        {
            return Position.Unreadable;
        }

        var belowLower = introduced is not null && version < introduced;
        var aboveUpper = (fixedVersion is not null && version >= fixedVersion) || (lastAffected is not null && version > lastAffected);
        return (belowLower, aboveUpper) switch
        {
            (false, false) => Position.Inside,
            (false, true) => Position.Above,
            (true, false) => Position.BelowNamedBound,
            // An inverted range (lower bound above the upper one) says nothing reliable.
            _ => Position.Unreadable,
        };
    }

    private enum Position
    {
        Inside,
        Above,
        BelowNamedBound,
        Unreadable,
    }
}
