using System.Text.RegularExpressions;

namespace Ardea.Intel.Sources.Ncsc;

/// <summary>CVE ids read from free text by pattern (master plan 6.5). Sequence numbers have four or more digits.</summary>
internal static partial class CveIds
{
    [GeneratedRegex(@"(?<![A-Za-z0-9])CVE-\d{4}-\d{4,}(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>Every distinct id in the texts, upper case, in order of first appearance.</summary>
    public static IReadOnlyList<string> Find(params string?[] texts)
    {
        var found = new List<string>();
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            foreach (Match match in Pattern().Matches(text))
            {
                var id = match.Value.ToUpperInvariant();
                if (!found.Contains(id))
                {
                    found.Add(id);
                }
            }
        }

        return found;
    }
}
