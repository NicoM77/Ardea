using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Versions;

internal sealed partial class NumericScheme : IVersionScheme
{
    private static readonly Dictionary<string, int> PreReleaseWords = new(StringComparer.Ordinal)
    {
        ["dev"] = 0,
        ["snapshot"] = 0,
        ["alpha"] = 1,
        ["beta"] = 2,
        ["milestone"] = 3,
        ["pre"] = 4,
        ["preview"] = 4,
        ["rc"] = 5,
        ["cr"] = 5,
    };

    public string Name => VersionSchemeNames.Numeric;

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

        var numbers = m.Groups["numbers"].Value.Split('.').Select(NumericText.Trim).ToArray();
        var suffixText = m.Groups["suffix"].Value.ToLowerInvariant();
        var tokens = Tokens().Matches(suffixText).Select(t => t.Value).ToArray();
        if (tokens is ["x"])
        {
            return false;
        }

        var preRank = tokens.Length > 0 && PreReleaseWords.TryGetValue(tokens[0], out var rank) ? rank : (int?)null;
        var normalized = string.Join('.', numbers) + (suffixText.Length == 0 ? string.Empty : m.Groups["separator"].Value + suffixText);
        version = new NumericVersion(trimmed, normalized, numbers, preRank, tokens);
        return true;
    }

    [GeneratedRegex(@"^[vV]?(?<numbers>\d{1,30}(?:\.\d{1,30})*)(?:(?<separator>[-_~. ]?)(?<suffix>[A-Za-z0-9]+(?:[-_.][A-Za-z0-9]+)*))?(?:\+[A-Za-z0-9._-]*)?$")]
    private static partial Regex Pattern();

    [GeneratedRegex(@"\d+|[a-z]+")]
    private static partial Regex Tokens();

    private sealed class NumericVersion(string original, string normalized, string[] numbers, int? preRank, string[] suffix)
        : ParsedVersion(VersionSchemeNames.Numeric, original, normalized)
    {
        private string[] Numbers => numbers;

        private int? PreRank => preRank;

        private string[] Suffix => suffix;

        private int Kind => suffix.Length == 0 ? 0 : preRank is null ? 1 : -1;

        protected override int CompareToSameScheme(ParsedVersion other)
        {
            var o = (NumericVersion)other;
            var c = NumericText.ComparePadded(numbers, o.Numbers);
            if (c != 0)
            {
                return c;
            }

            c = Kind.CompareTo(o.Kind);
            if (c != 0 || Kind == 0)
            {
                return c;
            }

            if (Kind < 0)
            {
                c = preRank!.Value.CompareTo(o.PreRank!.Value);
                if (c != 0)
                {
                    return c;
                }
            }

            for (var i = Kind < 0 ? 1 : 0; i < Math.Max(suffix.Length, o.Suffix.Length); i++)
            {
                if (i >= suffix.Length || i >= o.Suffix.Length)
                {
                    return suffix.Length.CompareTo(o.Suffix.Length);
                }

                var numericA = char.IsAsciiDigit(suffix[i][0]);
                var numericB = char.IsAsciiDigit(o.Suffix[i][0]);
                c = numericA && numericB ? NumericText.Compare(suffix[i], o.Suffix[i])
                    : numericA != numericB ? (numericA ? 1 : -1)
                    : Math.Sign(string.CompareOrdinal(suffix[i], o.Suffix[i]));
                if (c != 0)
                {
                    return c;
                }
            }

            return 0;
        }
    }
}
