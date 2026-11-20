namespace Ardea.Catalog.Versions;

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

    public static string Trim(string digits)
    {
        var trimmed = digits.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

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
