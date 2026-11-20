using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Versions;

internal sealed partial class Pep440Scheme : IVersionScheme
{
    public string Name => VersionSchemeNames.Pep440;

    public bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out ParsedVersion? version)
    {
        version = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        var m = Pattern().Match(trimmed);
        if (!m.Success)
        {
            return false;
        }

        var epoch = m.Groups["epoch"].Success ? NumericText.Trim(m.Groups["epoch"].Value) : "0";
        var release = m.Groups["release"].Value.Split('.').Select(NumericText.Trim).ToArray();

        (int Letter, string Number)? pre = null;
        if (m.Groups["pre_l"].Success)
        {
            var letter = m.Groups["pre_l"].Value.ToLowerInvariant() switch
            {
                "a" or "alpha" => 0,
                "b" or "beta" => 1,
                _ => 2,
            };
            pre = (letter, m.Groups["pre_n"].Success ? NumericText.Trim(m.Groups["pre_n"].Value) : "0");
        }

        string? post = m.Groups["post_n1"].Success ? NumericText.Trim(m.Groups["post_n1"].Value)
            : m.Groups["post_l"].Success ? (m.Groups["post_n2"].Success ? NumericText.Trim(m.Groups["post_n2"].Value) : "0")
            : null;
        string? dev = m.Groups["dev_l"].Success ? (m.Groups["dev_n"].Success ? NumericText.Trim(m.Groups["dev_n"].Value) : "0") : null;
        var local = m.Groups["local"].Success
            ? m.Groups["local"].Value.ToLowerInvariant().Split(['-', '_', '.'])
            : null;

        var normalized = (epoch != "0" ? epoch + "!" : string.Empty)
            + string.Join('.', release)
            + (pre is { } p ? (p.Letter switch { 0 => "a", 1 => "b", _ => "rc" }) + p.Number : string.Empty)
            + (post is null ? string.Empty : ".post" + post)
            + (dev is null ? string.Empty : ".dev" + dev)
            + (local is null ? string.Empty : "+" + string.Join('.', local));
        version = new Pep440Version(trimmed, normalized, new Key(epoch, release, pre, post, dev, local));
        return true;
    }

    [GeneratedRegex(
        @"^v?(?:(?:(?<epoch>[0-9]+)!)?(?<release>[0-9]+(?:\.[0-9]+)*)(?<pre>[-_\.]?(?<pre_l>alpha|a|beta|b|preview|pre|c|rc)[-_\.]?(?<pre_n>[0-9]+)?)?(?<post>(?:-(?<post_n1>[0-9]+))|(?:[-_\.]?(?<post_l>post|rev|r)[-_\.]?(?<post_n2>[0-9]+)?))?(?<dev>[-_\.]?(?<dev_l>dev)[-_\.]?(?<dev_n>[0-9]+)?)?)(?:\+(?<local>[a-z0-9]+(?:[-_\.][a-z0-9]+)*))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    private sealed record Key(string Epoch, string[] Release, (int Letter, string Number)? Pre, string? Post, string? Dev, string[]? Local);

    private sealed class Pep440Version(string original, string normalized, Key key)
        : ParsedVersion(VersionSchemeNames.Pep440, original, normalized)
    {
        private Key Value => key;

        protected override int CompareToSameScheme(ParsedVersion other)
        {
            var a = key;
            var b = ((Pep440Version)other).Value;
            var c = NumericText.Compare(a.Epoch, b.Epoch);
            if (c != 0)
            {
                return c;
            }

            c = NumericText.ComparePadded(a.Release, b.Release);
            if (c != 0)
            {
                return c;
            }

            c = ComparePre(a, b);
            if (c != 0)
            {
                return c;
            }

            c = CompareOptional(a.Post, b.Post, missingFirst: true);
            if (c != 0)
            {
                return c;
            }

            c = CompareOptional(a.Dev, b.Dev, missingFirst: false);
            return c != 0 ? c : CompareLocal(a.Local, b.Local);
        }

        private static int ComparePre(Key a, Key b)
        {
            var rankA = PreRank(a);
            var rankB = PreRank(b);
            if (rankA != rankB)
            {
                return rankA.CompareTo(rankB);
            }

            if (rankA != 1)
            {
                return 0;
            }

            var c = a.Pre!.Value.Letter.CompareTo(b.Pre!.Value.Letter);
            return c != 0 ? c : NumericText.Compare(a.Pre.Value.Number, b.Pre.Value.Number);
        }

        private static int PreRank(Key k) =>
            k.Pre is not null ? 1 : k.Post is null && k.Dev is not null ? 0 : 2;

        private static int CompareOptional(string? a, string? b, bool missingFirst)
        {
            if (a is null || b is null)
            {
                return a is null && b is null ? 0 : (a is null ? -1 : 1) * (missingFirst ? 1 : -1);
            }

            return NumericText.Compare(a, b);
        }

        private static int CompareLocal(string[]? a, string[]? b)
        {
            if (a is null || b is null)
            {
                return a is null && b is null ? 0 : a is null ? -1 : 1;
            }

            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                var numericA = a[i].All(char.IsAsciiDigit);
                var numericB = b[i].All(char.IsAsciiDigit);
                var c = numericA && numericB ? NumericText.Compare(a[i], b[i])
                    : numericA != numericB ? (numericA ? 1 : -1)
                    : string.CompareOrdinal(a[i], b[i]);
                if (c != 0)
                {
                    return Math.Sign(c);
                }
            }

            return a.Length.CompareTo(b.Length);
        }
    }
}
