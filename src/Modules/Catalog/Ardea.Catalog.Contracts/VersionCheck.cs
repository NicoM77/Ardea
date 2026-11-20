namespace Ardea.Catalog.Contracts;

public enum VersionVerdict
{
    Unknown,
    InRange,
    OutOfRange,
}

public enum VersionCheckReason
{
    InAffectedRange,

    BeforeEmptyRange,

    InUnaffectedRange,

    DefaultAffected,

    DefaultUnaffected,

    AboveAllRanges,

    OutsideNamedBounds,

    NotInAnyRange,

    InUnknownRange,

    NoInstalledVersion,

    InstalledVersionNotReadable,

    RangeNotReadable,

    NoComparableRange,

    FixNamedInTextAboveInstalled,

    NoScheme,
}

public readonly record struct VersionCheck(VersionVerdict Verdict, VersionCheckReason Reason)
{
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
            .Where(r => !r.IsCommitRange && !r.NamesNoVersion)
            .Select(r => (Range: r, Position: Locate(scheme, version, r)))
            .ToList();
        if (positions.Count == 0 && ranges.Count > 0)
        {
            return defaultStatus == VersionStatus.Affected && ranges.All(r => r.Status != VersionStatus.Unaffected)
                ? new(VersionVerdict.InRange, VersionCheckReason.DefaultAffected)
                : Unknown(VersionCheckReason.NoComparableRange);
        }

        if (positions.Exists(p => p.Position == Position.Inside && p.Range.Status == VersionStatus.Affected))
        {
            return new(VersionVerdict.InRange, VersionCheckReason.InAffectedRange);
        }

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

        if (positions.Exists(p => p.Position == Position.Unreadable))
        {
            return Unknown(VersionCheckReason.RangeNotReadable);
        }

        if (positions.Exists(p => p.Position == Position.BeforeEmpty && p.Range.Status == VersionStatus.Affected))
        {
            return new(VersionVerdict.InRange, VersionCheckReason.BeforeEmptyRange);
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

        if (aboveAll)
        {
            return proseNamesLaterFix
                ? Unknown(VersionCheckReason.FixNamedInTextAboveInstalled)
                : new(VersionVerdict.OutOfRange, VersionCheckReason.AboveAllRanges);
        }

        if (affected.TrueForAll(p => p.Position is Position.Above or Position.BelowNamedBound))
        {
            return new(VersionVerdict.OutOfRange, VersionCheckReason.OutsideNamedBounds);
        }

        return Unknown(VersionCheckReason.NotInAnyRange);
    }

    private static VersionCheck Unknown(VersionCheckReason reason) => new(VersionVerdict.Unknown, reason);

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
        var line = VersionLines.Line(installed);
        return line.Length > 0 && !structured.Concat(named.Select(f => f.Text)).Any(fix =>
            scheme.TryParse(fix, out var parsed) && parsed <= version && VersionLines.InLine(fix, line));
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
            return upperReadable && aboveUpper ? Position.Above : Position.Unreadable;
        }

        if (!upperReadable)
        {
            return Position.Unreadable;
        }

        if (introduced is not null && fixedVersion is not null && lastAffected is null && introduced.CompareTo(fixedVersion) == 0)
        {
            return version < fixedVersion ? Position.BeforeEmpty : Position.Above;
        }

        var belowLower = introduced is not null && version < introduced;
        return (belowLower, aboveUpper) switch
        {
            (false, false) => Position.Inside,
            (false, true) => Position.Above,
            (true, false) => Position.BelowNamedBound,
            _ => Position.Unreadable,
        };
    }

    private enum Position
    {
        Inside,
        Above,
        BelowNamedBound,

        BeforeEmpty,
        Unreadable,
    }
}
