using System.Globalization;

namespace Ardea.Catalog.Contracts;

public static class VersionLines
{
    private const int CanonicalParts = 3;

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

    public static bool InLine(string? version, IReadOnlyList<long> line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var numbers = LeadingNumbers(version);
        return line.Count > 0 && numbers.Length > 0 && line.Select((n, i) => (i < numbers.Length ? numbers[i] : 0) == n).All(same => same);
    }

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
