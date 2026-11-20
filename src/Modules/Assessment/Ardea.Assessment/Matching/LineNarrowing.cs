using Ardea.Catalog.Contracts;

namespace Ardea.Assessment.Matching;

/// <summary>
/// The narrowing by a version line (master plan 17.3; ADR 044 decision 5): a coarse component with a line («PostgreSQL 17»)
/// is hit by an entry unless every affected range the entry states for the product lies outside the line. A range whose
/// bounds the line's numbers cannot place (commits, wildcards, a bare «0»), and an entry without ranges, stay: a line
/// narrows only what the source rules out. Measured in task 5a.1 and moved here from its harness in task 5a.3.
/// </summary>
internal static class LineNarrowing
{
    /// <summary>Whether the entry's statements leave the line in.</summary>
    /// <param name="statements">The version statements of the strong hit, as the version check gives them.</param>
    /// <param name="line">The line's leading numbers, e.g. <c>[17]</c> or <c>[8, 3]</c>; empty keeps everything.</param>
    public static bool Keeps(IEnumerable<VersionStatement> statements, IReadOnlyList<long> line)
    {
        if (line.Count == 0)
        {
            return true;
        }

        var ranges = statements.SelectMany(s => s.Ranges).Where(r => r.Status == "affected").ToList();
        return ranges.Count == 0 || ranges.Exists(r => Touches(r, line) != false);
    }

    /// <summary>Whether a range overlaps the line; <c>null</c> when its bounds do not say.</summary>
    public static bool? Touches(RangeView range, IReadOnlyList<long> line)
    {
        ArgumentNullException.ThrowIfNull(range);
        long[] start = [.. line];
        long[] end = [.. line.Take(line.Count - 1), line[^1] + 1];
        if (new[] { range.Introduced, range.Fixed, range.LastAffected, range.Exact }.Any(IsCommit))
        {
            return null;
        }

        if (range.Exact is { } exact)
        {
            return exact == "0" || VersionLines.LeadingNumbers(exact).Length == 0 ? null : VersionLines.InLine(exact, line);
        }

        if (range.Introduced is null && range.Fixed is null && range.LastAffected is null)
        {
            return null;
        }

        var lowerOk = VersionLines.CompareNumbers(VersionLines.LeadingNumbers(range.Introduced), end) < 0;
        var upperOk = range.Fixed is not null && VersionLines.LeadingNumbers(range.Fixed) is { Length: > 0 } fixedAt ? VersionLines.CompareNumbers(fixedAt, start) > 0
            : range.LastAffected is not null && VersionLines.LeadingNumbers(range.LastAffected) is { Length: > 0 } lastAt ? VersionLines.CompareNumbers(lastAt, start) >= 0
            : true;
        return lowerOk && upperOk;
    }

    /// <summary>
    /// A Git commit (CVE records of the Linux kernel state ranges as <c>1da177e4c3f4</c> to <c>4f2e1a9b…</c>): read as a
    /// version, its leading digits would place it at 1 or 4. A hash of digits alone is left to the numbers, as a version may be one.
    /// </summary>
    private static bool IsCommit(string? bound) => VersionRanges.IsCommitHash(bound) && bound!.Any(char.IsAsciiLetter);
}
