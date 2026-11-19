using System.Globalization;

namespace Ardea.Catalog.Contracts;

/// <summary>
/// Release lines of versions, read from their leading numbers whatever the scheme: the line of 6.3.4 is
/// 6.3. Versions are first brought to a canonical length (at least three parts, trailing zeros beyond the
/// third dropped), so 16.4 and 16.4.0 lie in the same line 16.4, and 6.3.1.0 in the line 6.3.
/// </summary>
public static class VersionLines
{
    private const int CanonicalParts = 3;

    /// <summary>The dot-separated numbers a version starts with: 11.0.0 for <c>v11.0.0-M2</c>, none for <c>latest</c>.</summary>
    public static long[] LeadingNumbers(string? version)
    {
        var numbers = new List<long>();
        if (string.IsNullOrWhiteSpace(version))
        {
            return [];
        }

        foreach (var part in version.Trim().TrimStart('v', 'V').Split('.'))
        {
            var digits = new string([.. part.TakeWhile(char.IsAsciiDigit)]);
            if (digits.Length is 0 or > 18)
            {
                break;
            }

            numbers.Add(long.Parse(digits, CultureInfo.InvariantCulture));
            if (digits.Length < part.Length)
            {
                break;
            }
        }

        return [.. numbers];
    }

    /// <summary>The line of a version: its canonical numbers without the last; empty when it has no leading number.</summary>
    public static long[] Line(string? version)
    {
        var numbers = LeadingNumbers(version);
        if (numbers.Length == 0)
        {
            return [];
        }

        var canonical = numbers.Concat(Enumerable.Repeat(0L, Math.Max(0, CanonicalParts - numbers.Length))).ToList();
        while (canonical.Count > CanonicalParts && canonical[^1] == 0)
        {
            canonical.RemoveAt(canonical.Count - 1);
        }

        return [.. canonical.Take(canonical.Count - 1)];
    }

    /// <summary>Whether a version lies in a line; missing parts count as 0 (1.20 is in the line 1.20, not in 1.19).</summary>
    public static bool InLine(string? version, IReadOnlyList<long> line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var numbers = LeadingNumbers(version);
        return line.Count > 0 && numbers.Length > 0 && line.Select((n, i) => (i < numbers.Length ? numbers[i] : 0) == n).All(same => same);
    }

    /// <summary>Compares the leading numbers of two versions, missing parts as 0.</summary>
    public static int CompareNumbers(IReadOnlyList<long> left, IReadOnlyList<long> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        for (var i = 0; i < Math.Max(left.Count, right.Count); i++)
        {
            var c = (i < left.Count ? left[i] : 0).CompareTo(i < right.Count ? right[i] : 0);
            if (c != 0)
            {
                return c;
            }
        }

        return 0;
    }
}
