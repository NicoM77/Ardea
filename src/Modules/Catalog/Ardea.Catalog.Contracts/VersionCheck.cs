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

    /// <summary>
    /// Rows 4 and 5 would say «out of range» because the version lies above every affected range, but the
    /// entry's text names a later fix and no fix of the version's own line at or below it (master plan 6.6).
    /// </summary>
    FixNamedInTextAboveInstalled,

    /// <summary>No version scheme compares these versions (Debian, date-based, …).</summary>
    NoScheme,
}

/// <summary>The verdict with the rule that gave it.</summary>
public readonly record struct VersionCheck(VersionVerdict Verdict, VersionCheckReason Reason)
{
    /// <summary>
    /// Checks an installed version against the ranges of one affected product (master plan 6.6). The
    /// rows of the table apply in order. Ranges of Git commits (<see cref="VersionRange.IsCommitRange"/>) are
    /// skipped; when nothing else is left, the default status cannot be applied either, because the version may
    /// lie inside one of the commit ranges, and the record names no comparable range.
    /// </summary>
    /// <param name="scheme">The scheme to compare with; <c>null</c> gives <see cref="VersionCheckReason.NoScheme"/>.</param>
    /// <param name="installed">The version in the profile.</param>
    /// <param name="ranges">The product's ranges from one record.</param>
    /// <param name="defaultStatus">The record's status for every version it does not name; <see cref="VersionStatus.Unknown"/> when it names none.</param>
    /// <param name="fixesInText">
    /// Versions the entry's text names as fixes. When the version lies above every affected range and rows 4 or 5
    /// would say «out of range», a later fix named in the text makes it «unknown» instead
    /// (<see cref="VersionCheckReason.FixNamedInTextAboveInstalled"/>): a conflict between structured data and
    /// prose never yields «not affected». A fix at or below the version in the version's own line, in the ranges
    /// or the text, means that line is fixed; then the structured statement stands.
    /// </param>
    public static VersionCheck Evaluate(
        IVersionScheme? scheme, string? installed, IReadOnlyCollection<VersionRange> ranges, VersionStatus defaultStatus, IReadOnlyCollection<string>? fixesInText = null)
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
            .Where(r => !r.IsCommitRange)
            .Select(r => (Range: r, Position: Locate(scheme, version, r)))
            .ToList();
        if (positions.Count == 0 && ranges.Count > 0)
        {
            return Unknown(VersionCheckReason.NoComparableRange);
        }

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

        var affected = positions.Where(p => p.Range.Status == VersionStatus.Affected).ToList();
        var aboveAll = affected.Count > 0 && affected.TrueForAll(p => p.Position == Position.Above);
        var proseNamesLaterFix = aboveAll && FixNamedInTextAbove(scheme, version, installed.Trim(), positions.Select(p => p.Range), fixesInText);
        switch (defaultStatus)
        {
            case VersionStatus.Affected:
                return new(VersionVerdict.InRange, VersionCheckReason.DefaultAffected);
            case VersionStatus.Unaffected:
                return proseNamesLaterFix
                    ? Unknown(VersionCheckReason.FixNamedInTextAboveInstalled)
                    : new(VersionVerdict.OutOfRange, VersionCheckReason.DefaultUnaffected);
        }

        if (affected.Count == 0)
        {
            return Unknown(positions.Count == 0 ? VersionCheckReason.NoComparableRange : VersionCheckReason.NotInAnyRange);
        }

        // Row 5, the deliberate interpretation: above every fix or upper bound, unless the text names a later fix.
        if (aboveAll)
        {
            return proseNamesLaterFix
                ? Unknown(VersionCheckReason.FixNamedInTextAboveInstalled)
                : new(VersionVerdict.OutOfRange, VersionCheckReason.AboveAllRanges);
        }

        // Below a range counts only when the record names that lower bound itself.
        if (affected.TrueForAll(p => p.Position is Position.Above or Position.BelowNamedBound))
        {
            return new(VersionVerdict.OutOfRange, VersionCheckReason.OutsideNamedBounds);
        }

        return Unknown(VersionCheckReason.NotInAnyRange);
    }

    private static VersionCheck Unknown(VersionCheckReason reason) => new(VersionVerdict.Unknown, reason);

    /// <summary>
    /// Whether the text names a fix above the installed version while nothing names a fix of the installed
    /// version's own line at or below it. The line is the version without its last part (6.3 for 6.3.4): a
    /// record listing «6.4.0, 6.3.3 or 5.19.12» as fixes has fixed 6.3.4 with 6.3.3, whatever 6.4.0 says.
    /// </summary>
    private static bool FixNamedInTextAbove(
        IVersionScheme scheme, ParsedVersion version, string installed, IEnumerable<VersionRange> ranges, IReadOnlyCollection<string>? fixesInText)
    {
        if (fixesInText is not { Count: > 0 })
        {
            return false;
        }

        var named = fixesInText
            .Select(text => (Text: text, Parsed: scheme.TryParse(text, out var parsed) ? parsed : null))
            .Where(f => f.Parsed is not null)
            .ToList();
        if (!named.Exists(f => f.Parsed! > version))
        {
            return false;
        }

        var structured = ranges.SelectMany(r => r.Status switch
        {
            VersionStatus.Affected => new[] { r.Fixed },
            VersionStatus.Unaffected => [r.Introduced, r.Exact],
            _ => [],
        }).OfType<string>();
        var line = Line(installed);
        return line.Length > 0 && !structured.Concat(named.Select(f => f.Text)).Any(fix =>
            scheme.TryParse(fix, out var parsed) && parsed <= version && SameLine(fix, line));
    }

    /// <summary>The leading numbers of a version without the last one: 6.3 for 6.3.4, 26 for 26.</summary>
    private static long[] Line(string version)
    {
        var numbers = LeadingNumbers(version);
        return numbers.Length > 1 ? numbers[..^1] : numbers;
    }

    /// <summary>Whether a version belongs to a line; missing parts count as 0 (1.20 is in line 1.20, not in 1.19).</summary>
    private static bool SameLine(string version, long[] line)
    {
        var numbers = LeadingNumbers(version);
        return line.Select((n, i) => (i < numbers.Length ? numbers[i] : 0) == n).All(same => same);
    }

    /// <summary>The dot-separated numbers a version starts with: 11.0.0 for 11.0.0-M2, none for «latest».</summary>
    private static long[] LeadingNumbers(string version)
    {
        var numbers = new List<long>();
        foreach (var part in version.Trim().TrimStart('v', 'V').Split('.'))
        {
            var digits = new string([.. part.TakeWhile(char.IsAsciiDigit)]);
            if (digits.Length is 0 or > 18)
            {
                break;
            }

            numbers.Add(long.Parse(digits, System.Globalization.CultureInfo.InvariantCulture));
            if (digits.Length < part.Length)
            {
                break;
            }
        }

        return [.. numbers];
    }

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
        var upperReadable = (range.Fixed is null || scheme.TryParse(range.Fixed, out fixedVersion))
            && (range.LastAffected is null || scheme.TryParse(range.LastAffected, out lastAffected));
        var aboveUpper = (fixedVersion is not null && version >= fixedVersion) || (lastAffected is not null && version > lastAffected);
        if (range.Introduced is not null && !scheme.TryParse(range.Introduced, out introduced))
        {
            // Whatever an unreadable lower bound says («NOT_YET_AVAILABLE»), a version at or past a readable fix is above the range.
            return upperReadable && aboveUpper ? Position.Above : Position.Unreadable;
        }

        if (!upperReadable)
        {
            return Position.Unreadable;
        }

        var belowLower = introduced is not null && version < introduced;
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
