using System.Text.RegularExpressions;

namespace Ardea.Intel.Correlation;

internal static partial class FixVersionText
{
    private const int MaxVersions = 20;

    private const string Version = @"v?(\d+(?:\.\d+)+(?:(?:[-+~][0-9A-Za-z]+|[A-Za-z][0-9A-Za-z]*)(?:\.[0-9A-Za-z]+)*)?)(?!\.?[0-9A-Za-z*])";

    private const string MoreVersions =
        @"((?:\s*(?:,\s*(?:or|and)?|or|and|/)\s*(?:version\s+)?v?\d+(?:\.\d+)+(?:(?:[-+~][0-9A-Za-z]+|[A-Za-z][0-9A-Za-z]*)(?:\.[0-9A-Za-z]+)*)?(?!\.?[0-9A-Za-z*]))*)";

    public static IReadOnlyList<string> Extract(IEnumerable<string?> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var versions = new List<string>();
        foreach (var text in texts.OfType<string>())
        {
            foreach (var pattern in new[] { FixWord(), UpgradeToProduct(), UpgradeProductTo() })
            {
                try
                {
                    foreach (Match match in pattern.Matches(text))
                    {
                        Add(versions, match.Groups[1].Value);
                        foreach (Match more in ListedVersion().Matches(match.Groups[2].Value))
                        {
                            Add(versions, more.Groups[1].Value);
                        }
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                }
            }
        }

        return versions.Take(MaxVersions).ToList();
    }

    private static void Add(List<string> versions, string version)
    {
        if (version.Length is > 0 and <= 50 && !versions.Contains(version, StringComparer.Ordinal))
        {
            versions.Add(version);
        }
    }

    [GeneratedRegex(
        @"\b(?:fixed in|fixes? (?:it|this|the issue) in|upgrade to|upgrading to|update to|updating to|patched in|resolved in|addressed in|before|prior to|earlier than)\s+(?:(?:the\s+)?(?:version|release)s?\s+)?"
            + Version + MoreVersions,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex FixWord();

    [GeneratedRegex(
        @"\b(?:upgrade|update|upgrading|updating)\s+to\s+(?:[A-Za-z][\w.-]*\s+){1,4}?(?:version|release)s?\s+" + Version + MoreVersions,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex UpgradeToProduct();

    [GeneratedRegex(
        @"\b(?:upgrade|update)\s+(?:[A-Za-z][\w.-]*\s+){1,4}?to\s+(?:(?:the\s+)?(?:version|release)s?\s+)?" + Version + MoreVersions,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex UpgradeProductTo();

    [GeneratedRegex(Version, RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ListedVersion();
}
