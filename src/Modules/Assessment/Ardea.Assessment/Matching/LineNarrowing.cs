using Ardea.Catalog.Contracts;

namespace Ardea.Assessment.Matching;

internal static class LineNarrowing
{
    public static bool Keeps(IEnumerable<VersionStatement> statements, IReadOnlyList<long> line)
    {
        if (line.Count == 0)
        {
            return true;
        }

        var ranges = statements.SelectMany(s => s.Ranges).Where(r => r.Status == "affected").ToList();
        return ranges.Count == 0 || ranges.Exists(r => Touches(r, line) != false);
    }

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

    private static bool IsCommit(string? bound) => VersionRanges.IsCommitHash(bound) && bound!.Any(char.IsAsciiLetter);
}
