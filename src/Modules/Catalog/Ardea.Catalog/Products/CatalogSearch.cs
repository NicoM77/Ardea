using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Products;

/// <summary>
/// The input help over the catalogue (task 2.4): what someone types, with spaces, punctuation, version
/// numbers and the odd typo, finds the product they mean first. Deterministic and in memory; tiers
/// from strong to weak, the first one that matches counts:
/// <list type="number">
/// <item>the text without spaces and punctuation equals a key, name or alias, then a CPE product, package or image;</item>
/// <item>every word is a whole word of a key, name or alias;</item>
/// <item>a key, name or alias starts with it, the closer in length the better;</item>
/// <item>every word is a whole word of these or of the vendor, CPE pair, package or image;</item>
/// <item>every word starts a word of these;</item>
/// <item>a CPE product, package or image starts with it; it occurs inside any of them;</item>
/// <item>it is a few typos away from a key, name or alias (bounded edit distance).</item>
/// </list>
/// Version numbers typed after a name (<c>.net 8</c>, <c>ubuntu 22.04</c>) are tried with and without.
/// </summary>
internal sealed partial class CatalogSearch
{
    private const int NoMatch = int.MaxValue;

    private readonly List<Entry> _entries;

    public CatalogSearch(IEnumerable<CatalogProduct> products) => _entries = [.. products.Select(Entry.Create)];

    public IReadOnlyList<CatalogProduct> Search(string text, int limit)
    {
        var words = Words(text);
        if (words.Count == 0 || limit <= 0)
        {
            return [];
        }

        List<string[]> queries = [[.. words]];
        var named = words.Where(w => !w.All(char.IsAsciiDigit)).ToArray();
        if (named.Length > 0 && named.Length < words.Count)
        {
            queries.Add(named);
        }

        return _entries
            .Select(e => (e.Product, Rank: queries.Select(q => e.Rank(q, string.Concat(q))).Min()))
            .Where(x => x.Rank.Tier != NoMatch)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Product.Origin)
            .ThenBy(x => x.Product.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Product.Key, StringComparer.Ordinal)
            .Take(limit)
            .Select(x => x.Product)
            .ToList();
    }

    /// <summary>Lower-case words of letters and digits, without diacritics; everything else separates.</summary>
    public static List<string> Words(string? text)
    {
        var words = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return words;
        }

        var current = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                current.Append(char.ToLowerInvariant(c));
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }

    /// <summary>
    /// Optimal string alignment distance (insertions, deletions, substitutions, swaps of neighbours),
    /// or <paramref name="bound"/> + 1 once it is certain to exceed the bound.
    /// </summary>
    public static int Distance(string a, string b, int bound)
    {
        if (Math.Abs(a.Length - b.Length) > bound)
        {
            return bound + 1;
        }

        var rows = new int[3][];
        for (var r = 0; r < 3; r++)
        {
            rows[r] = new int[b.Length + 1];
        }

        for (var j = 0; j <= b.Length; j++)
        {
            rows[0][j] = j;
        }

        // Row i lives in rows[i % 3]; the swap of neighbours looks two rows back.
        for (var i = 1; i <= a.Length; i++)
        {
            var (before, previous, row) = (rows[(i + 1) % 3], rows[(i + 2) % 3], rows[i % 3]);
            row[0] = i;
            var best = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var value = Math.Min(Math.Min(previous[j] + 1, row[j - 1] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    value = Math.Min(value, before[j - 2] + 1);
                }

                row[j] = value;
                best = Math.Min(best, value);
            }

            if (best > bound)
            {
                return bound + 1;
            }
        }

        return Math.Min(rows[a.Length % 3][b.Length], bound + 1);
    }

    /// <summary>Typos allowed for a text of this length: none up to three characters, one up to seven, then two.</summary>
    private static int TypoBound(int length) => length <= 3 ? 0 : length <= 7 ? 1 : 2;

    [GeneratedRegex(@"\{[a-z]+\}|\*", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholders();

    private sealed class Entry
    {
        public required CatalogProduct Product { get; init; }

        /// <summary>Key, name and aliases, each as one word without separators.</summary>
        public required string[] Names { get; init; }

        /// <summary>CPE products, packages and images, each as one word without separators.</summary>
        public required string[] Others { get; init; }

        public required HashSet<string> NameWords { get; init; }

        /// <summary>Words of the names, the vendor spellings, CPE pairs, packages and images.</summary>
        public required HashSet<string> AllWords { get; init; }

        public static Entry Create(CatalogProduct product)
        {
            var names = new List<string> { product.Key, product.Name };
            names.AddRange(product.Aliases.SelectMany(a => a.Names).Select(n => Placeholders().Replace(n, " ")));

            var others = new List<string>();
            others.AddRange(product.CpePairs.Select(p => p.Product));
            others.AddRange(product.Packages.Select(p => p.Type == "maven" ? p.Name[(p.Name.LastIndexOf('/') + 1)..] : p.Name));
            foreach (var image in product.ImageNames)
            {
                var parts = image.Split('/');
                var hosted = parts.Length > 1 && parts[0].Contains('.', StringComparison.Ordinal);
                others.Add(image);
                others.Add(string.Join(' ', parts.Skip(hosted ? Math.Max(1, parts.Length - 2) : parts.Length - 1)));
            }

            var vendors = new List<string?> { product.Vendor };
            vendors.AddRange(product.Aliases.Select(a => a.Vendor));
            vendors.AddRange(product.CpePairs.Select(p => p.Vendor));

            var nameWords = names.SelectMany(Words).ToHashSet(StringComparer.Ordinal);
            return new Entry
            {
                Product = product,
                Names = Compact(names),
                Others = Compact(others),
                NameWords = nameWords,
                AllWords = [.. nameWords, .. others.Concat(vendors).SelectMany(Words)],
            };
        }

        /// <summary>Tier and, within it, distance (lower is better) of the query's words and their joined form.</summary>
        public (int Tier, int Distance) Rank(string[] words, string joined)
        {
            if (Array.IndexOf(Names, joined) >= 0)
            {
                return (0, 0);
            }

            if (Array.IndexOf(Others, joined) >= 0)
            {
                return (1, 0);
            }

            if (words.All(NameWords.Contains))
            {
                return (2, 0);
            }

            var prefix = Names.Where(n => n.StartsWith(joined, StringComparison.Ordinal)).Select(n => n.Length - joined.Length).DefaultIfEmpty(NoMatch).Min();
            if (prefix != NoMatch)
            {
                return (3, prefix);
            }

            if (words.All(AllWords.Contains))
            {
                return (4, 0);
            }

            if (words.All(w => NameWords.Any(n => n.StartsWith(w, StringComparison.Ordinal))))
            {
                return (5, 0);
            }

            if (words.All(w => AllWords.Any(n => n.StartsWith(w, StringComparison.Ordinal))))
            {
                return (6, 0);
            }

            prefix = Others.Where(n => n.StartsWith(joined, StringComparison.Ordinal)).Select(n => n.Length - joined.Length).DefaultIfEmpty(NoMatch).Min();
            if (prefix != NoMatch)
            {
                return (7, prefix);
            }

            if (joined.Length >= 3 && Names.Concat(Others).Any(n => n.Contains(joined, StringComparison.Ordinal)))
            {
                return (8, 0);
            }

            var bound = TypoBound(joined.Length);
            var distance = bound == 0 ? NoMatch : Names.Select(n => Distance(joined, n, bound)).Where(d => d <= bound).DefaultIfEmpty(NoMatch).Min();
            return distance == NoMatch ? (NoMatch, 0) : (9, distance);
        }

        private static string[] Compact(IEnumerable<string> texts) =>
            [.. texts.Select(t => string.Concat(Words(t))).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal)];
    }
}
