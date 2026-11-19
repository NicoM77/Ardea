using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ardea.Catalog.Contracts;
using Ardea.Catalog.Products;

namespace Ardea.Catalog.Learning;

/// <summary>
/// The checks every learned entry passes before it is stored, whoever proposed it (master plan 6.9 step 4): a target
/// that exists, or a well-formed new product whose key and spellings no entry has, with known kind and version scheme,
/// existing platform and related products, and CPE pairs no entry owns. A failed check names the rule, never the text.
/// </summary>
internal static partial class ProductDrafts
{
    public const int NameLength = 100;
    public const int ReasoningLength = 300;

    /// <summary>The kinds as stored and as the model names them: <c>os</c>, <c>runtime</c>, ….</summary>
    public static readonly string[] Kinds = [.. Enum.GetNames<ProductKind>().Select(k => k.ToLowerInvariant())];

    /// <summary>The version schemes the catalogue knows.</summary>
    public static readonly string[] Schemes = [.. Versions.VersionSchemeRegistry.Instance.All.Select(s => s.Name).Order(StringComparer.Ordinal)];

    /// <summary>The key of a new product: the name in lower case ASCII words joined by <c>-</c>, else with the vendor before it.</summary>
    public static string? KeyFor(string name, string? vendor, Func<string, bool> taken)
    {
        var plain = Kebab(name);
        if (plain is null)
        {
            return null;
        }

        if (!taken(plain))
        {
            return plain;
        }

        var withVendor = Kebab(vendor) is { } v && !plain.StartsWith(v + "-", StringComparison.Ordinal) ? Cut(v + "-" + plain) : null;
        return withVendor is not null && !taken(withVendor) ? withVendor : null;
    }

    /// <summary>
    /// Checks a new product against the catalogue in effect and the keys every row of <c>catalog.product</c> holds, also
    /// withdrawn ones. <paramref name="allowedCpe"/> limits the CPE pairs to those of the input (the model's), or
    /// <c>null</c> for any well-formed pair (an administrator's).
    /// </summary>
    public static string? Problem(ProposedProduct product, CatalogLayers catalog, ISet<string> keys, ISet<string>? allowedCpe, ISet<string>? allowedRelated)
    {
        if (!ProductKey.TryParse(product.Key, out _) || !KeyPattern().IsMatch(product.Key) || keys.Contains(product.Key) || catalog.FindByKey(product.Key) is not null)
        {
            return "key";
        }

        if (!Text(product.Name, NameLength) || (product.Vendor is not null && !Text(product.Vendor, NameLength)))
        {
            return "name";
        }

        if (!Kinds.Contains(product.Kind, StringComparer.Ordinal))
        {
            return "kind";
        }

        if (!Schemes.Contains(product.VersionScheme, StringComparer.Ordinal))
        {
            return "version_scheme";
        }

        // The name is new under every vendor; so is an alias without vendor. An alias under a vendor (the record's own
        // spelling) only must not find anything under that vendor: curated entries win every other lookup anyway.
        if (catalog.All.NameTaken(product.Name)
            || product.Aliases.Any(a => a.Names.Any(n => ProductKey.NormalizeVendor(a.Vendor) is null ? catalog.All.NameTaken(n) : catalog.FindByName(a.Vendor, n) is not null)))
        {
            return "name_taken";
        }

        foreach (var text in product.Cpe)
        {
            if (!CpePair.TryParse(text, out var pair) || pair.ToString() != text || (allowedCpe is not null && !allowedCpe.Contains(text)) || catalog.FindByCpe(pair) is not null)
            {
                return "cpe";
            }
        }

        foreach (var text in product.Packages)
        {
            if (!PackageCoordinate.TryParsePurl(text, out var package, out var version) || version is not null || catalog.FindByPackage(package.Type, package.Name) is not null)
            {
                return "package";
            }
        }

        if (product.Aliases.Any(a => a.Names.Count == 0 || a.Names.Any(n => !Text(n, NameLength * 2))))
        {
            return "alias";
        }

        foreach (var related in product.Related.Append(product.Platform).OfType<string>())
        {
            if (related == product.Key || catalog.FindByKey(related) is null || (allowedRelated is not null && !allowedRelated.Contains(related)))
            {
                return "related";
            }
        }

        if (product.Kind == "plugin" && product.Platform is null)
        {
            return "platform";
        }

        return null;
    }

    /// <summary>
    /// A short single-line text without placeholders of name patterns, markup, addresses or control characters: what a
    /// name, a vendor or a reason may be.
    /// </summary>
    public static bool Text(string? text, int length) =>
        !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= length && !text.Any(char.IsControl)
        && !text.Contains('*', StringComparison.Ordinal) && !text.Contains('{', StringComparison.Ordinal) && !text.Contains('}', StringComparison.Ordinal)
        && !Address().IsMatch(text);

    /// <summary>
    /// Whether a source's product statement reads like an instruction to a model (master plan 6.9 «Warum das sicher genug
    /// ist»): such a statement is still mapped and asked about, but nothing of it takes effect without a person.
    /// </summary>
    public static bool Suspicious(string? vendor, string product) =>
        new[] { vendor, product }.OfType<string>().Any(t => t.Length > 120 || t.Any(char.IsControl) || Markup().IsMatch(t) || Instruction().IsMatch(t));

    /// <summary>The comparable form of a CPE pair as the catalogue writes it, or <c>null</c>.</summary>
    public static string? CpeText(string text) => CpePair.TryParse(text, out var pair) ? pair.ToString() : null;

    private static string? Kebab(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(c);
            if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                builder.Append(lower);
            }
            else if (lower is '.' or '\'' or '’' && builder.Length > 0 && builder[^1] != '-')
            {
                // «Node.js», «O'Reilly»: punctuation inside a word joins rather than separates, as in product keys.
                continue;
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var key = Cut(builder.ToString().Trim('-'));
        return key.Length == 0 ? null : key;
    }

    private static string Cut(string key) => key.Length <= 60 ? key : key[..60].TrimEnd('-');

    [GeneratedRegex("^[a-z0-9]+([.-][a-z0-9]+)*(/[a-z0-9]+([.-][a-z0-9]+)*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"[a-z][a-z0-9+.-]*://|\bwww\.|<\s*/?\s*[a-z]|@[a-z0-9-]+\.[a-z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Address();

    [GeneratedRegex("[<>{}\\[\\]`\"]|```", RegexOptions.CultureInvariant)]
    private static partial Regex Markup();

    [GeneratedRegex(
        @"\b(ignore|disregard|forget|override)\b.{0,40}\b(previous|above|prior|earlier|all|any|the)\b|\b(instructions?|prompt|system message|assistant|you are|you must|answer with|respond with|alias_of|new_product|confidence)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Instruction();
}
