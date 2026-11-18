namespace Ardea.Catalog.Versions;

/// <summary>Compares runs of digits of any length by value, without parsing them into a fixed-size number.</summary>
internal static class NumericText
{
    public static int Compare(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        left = left.TrimStart('0');
        right = right.TrimStart('0');
        return left.Length != right.Length
            ? left.Length.CompareTo(right.Length)
            : Math.Sign(left.SequenceCompareTo(right));
    }

    /// <summary>The digits without leading zeros; <c>0</c> for zero.</summary>
    public static string Trim(string digits)
    {
        var trimmed = digits.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    /// <summary>Lexicographic order of number lists; a shorter list is padded with zeros.</summary>
    public static int ComparePadded(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        for (var i = 0; i < Math.Max(left.Count, right.Count); i++)
        {
            var c = Compare(i < left.Count ? left[i] : "0", i < right.Count ? right[i] : "0");
            if (c != 0)
            {
                return c;
            }
        }

        return 0;
    }
}
