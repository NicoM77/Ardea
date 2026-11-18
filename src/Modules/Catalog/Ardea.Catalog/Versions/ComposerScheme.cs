using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Versions;

/// <summary>
/// Packagist versions as Composer reads them (<c>VersionParser</c>, then PHP <c>version_compare</c>):
/// SemVer-like, but with up to four numbers, an optional <c>v</c>, and stability suffixes in the order
/// dev &lt; alpha (a) &lt; beta (b) &lt; RC &lt; stable &lt; patch (p, pl), case-insensitive. A trailing
/// <c>-dev</c> sorts before the same version without it. Branches (<c>dev-main</c>, <c>2.x-dev</c>) are no versions.
/// </summary>
internal sealed partial class ComposerScheme : IVersionScheme
{
    public string Name => VersionSchemeNames.Packagist;

    public bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out ParsedVersion? version)
    {
        version = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        var match = Pattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var numbers = new[] { 1, 2, 3, 4 }
            .Select(i => match.Groups[i].Success ? NumericText.Trim(match.Groups[i].Value.TrimStart('.')) : "0")
            .ToArray();
        var stability = match.Groups["stability"].Success
            ? match.Groups["stability"].Value.ToLowerInvariant() switch
            {
                "a" or "alpha" => Stability.Alpha,
                "b" or "beta" => Stability.Beta,
                "rc" => Stability.ReleaseCandidate,
                "p" or "pl" or "patch" => Stability.Patch,
                _ => Stability.Stable,
            }
            : Stability.Stable;
        var stabilityNumbers = DigitRuns().Matches(match.Groups["stabilityNumber"].Value).Select(m => NumericText.Trim(m.Value)).ToArray();
        var dev = match.Groups["dev"].Success;
        if (stability == Stability.Stable && dev && stabilityNumbers.Length == 0)
        {
            // «1.0.0-dev»: the development stability of the version itself.
            stability = Stability.Dev;
            dev = false;
        }

        var normalized = string.Join('.', numbers)
            + (stability == Stability.Stable ? string.Empty : "-" + StabilityName(stability) + string.Join('.', stabilityNumbers))
            + (dev ? "-dev" : string.Empty);
        version = new ComposerVersion(trimmed, normalized, numbers, stability, stabilityNumbers, dev);
        return true;
    }

    private static string StabilityName(Stability stability) => stability switch
    {
        Stability.Dev => "dev",
        Stability.Alpha => "alpha",
        Stability.Beta => "beta",
        Stability.ReleaseCandidate => "RC",
        _ => "patch",
    };

    [GeneratedRegex(
        @"^v?(\d{1,18})(\.\d{1,18})?(\.\d{1,18})?(\.\d{1,18})?(?:[._-]?(?<stability>stable|beta|b|rc|alpha|a|patch|pl|p)(?<stabilityNumber>(?:[.-]?\d{1,18})*))?(?<dev>[.-]?dev)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitRuns();

    private enum Stability
    {
        Dev,
        Alpha,
        Beta,
        ReleaseCandidate,
        Stable,
        Patch,
    }

    private sealed class ComposerVersion(string original, string normalized, string[] numbers, Stability stability, string[] stabilityNumbers, bool dev)
        : ParsedVersion(VersionSchemeNames.Packagist, original, normalized)
    {
        private string[] Numbers => numbers;

        private Stability Level => stability;

        private string[] StabilityNumbers => stabilityNumbers;

        private bool Dev => dev;

        protected override int CompareToSameScheme(ParsedVersion other)
        {
            var o = (ComposerVersion)other;
            var c = NumericText.ComparePadded(numbers, o.Numbers);
            if (c != 0)
            {
                return c;
            }

            c = stability.CompareTo(o.Level);
            if (c != 0)
            {
                return c;
            }

            // beta < beta1 < beta1.1 < beta2: a missing number sorts first, as in version_compare.
            for (var i = 0; i < Math.Max(stabilityNumbers.Length, o.StabilityNumbers.Length); i++)
            {
                if (i >= stabilityNumbers.Length || i >= o.StabilityNumbers.Length)
                {
                    return stabilityNumbers.Length.CompareTo(o.StabilityNumbers.Length);
                }

                c = NumericText.Compare(stabilityNumbers[i], o.StabilityNumbers[i]);
                if (c != 0)
                {
                    return c;
                }
            }

            return o.Dev.CompareTo(dev);
        }
    }
}
