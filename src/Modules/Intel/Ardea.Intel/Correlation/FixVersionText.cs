using System.Text.RegularExpressions;

namespace Ardea.Intel.Correlation;

/// <summary>
/// The versions an entry's text names as fixes (master plan 6.6, ADR 033): «users are recommended to upgrade
/// to version 9.1.0», «fixed in 2.5.3», «Chrome prior to 157.0.8103.50», «Upgrade to FortiManager version
/// 8.0.1 or above». Read once at correlation from the description and the solutions; the version check uses
/// them only to keep a conflict between ranges and prose from turning into «not affected».
/// </summary>
/// <remarks>
/// Only words that name the fix count. «through 9.0.0» names the last affected version, and in descriptions
/// that list several products («FortiManager 7.6.0 through 7.6.7, FortiManager Cloud 8.0.0 through 8.0.1») it
/// often belongs to another product, so it is left out. A version needs at least two parts, which keeps
/// years and plain numbers («before 2024», «prior to 3 attempts») out.
/// </remarks>
internal static partial class FixVersionText
{
    private const int MaxVersions = 20;

    // A version ends where no further part follows: «2.46» is not taken out of «2.46.x».
    private const string Version = @"v?(\d+(?:\.\d+)+(?:(?:[-+~][0-9A-Za-z]+|[A-Za-z][0-9A-Za-z]*)(?:\.[0-9A-Za-z]+)*)?)(?!\.?[0-9A-Za-z*])";

    private const string MoreVersions =
        @"((?:\s*(?:,\s*(?:or|and)?|or|and|/)\s*(?:version\s+)?v?\d+(?:\.\d+)+(?:(?:[-+~][0-9A-Za-z]+|[A-Za-z][0-9A-Za-z]*)(?:\.[0-9A-Za-z]+)*)?(?!\.?[0-9A-Za-z*]))*)";

    /// <summary>The versions named as fixes, in order of appearance, each once.</summary>
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
                    // An unusual text is no reason to fail the correlation; it just yields nothing.
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

    /// <summary>«fixed in 2.5.3», «upgrade to version 9.1.0», «prior to 157.0.8103.50», «before 1.123.89, 2.45.5 and 3.0.1».</summary>
    [GeneratedRegex(
        @"\b(?:fixed in|fixes? (?:it|this|the issue) in|upgrade to|upgrading to|update to|updating to|patched in|resolved in|addressed in|before|prior to|earlier than)\s+(?:(?:the\s+)?(?:version|release)s?\s+)?"
            + Version + MoreVersions,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex FixWord();

    /// <summary>«Upgrade to FortiManager version 8.0.2 or above».</summary>
    [GeneratedRegex(
        @"\b(?:upgrade|update|upgrading|updating)\s+to\s+(?:[A-Za-z][\w.-]*\s+){1,4}?(?:version|release)s?\s+" + Version + MoreVersions,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex UpgradeToProduct();

    /// <summary>«Update Mattermost Desktop App to versions 6.4.0, 6.3.1.0, 6.2.4.0 or higher».</summary>
    [GeneratedRegex(
        @"\b(?:upgrade|update)\s+(?:[A-Za-z][\w.-]*\s+){1,4}?to\s+(?:(?:the\s+)?(?:version|release)s?\s+)?" + Version + MoreVersions,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex UpgradeProductTo();

    [GeneratedRegex(Version, RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ListedVersion();
}
