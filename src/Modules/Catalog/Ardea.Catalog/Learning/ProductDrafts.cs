using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ardea.Catalog.Contracts;
using Ardea.Catalog.Products;

namespace Ardea.Catalog.Learning;

internal static partial class ProductDrafts
{
    public const int NameLength = 100;
    public const int ReasoningLength = 300;

    public static readonly string[] Kinds = [.. Enum.GetNames<ProductKind>().Select(k => k.ToLowerInvariant())];

    public static readonly string[] Schemes = [.. Versions.VersionSchemeRegistry.Instance.All.Select(s => s.Name).Order(StringComparer.Ordinal)];

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

    public static bool Text(string? text, int length) =>
        !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= length && !text.Any(char.IsControl)
        && !text.Contains('*', StringComparison.Ordinal) && !text.Contains('{', StringComparison.Ordinal) && !text.Contains('}', StringComparison.Ordinal)
        && !Address().IsMatch(text);

    public static bool Suspicious(string? vendor, string product) =>
        new[] { vendor, product }.OfType<string>().Any(t => t.Length > 120 || t.Any(char.IsControl) || Markup().IsMatch(t) || Instruction().IsMatch(t));

    public static bool SuspiciousText(string? text) =>
        text is not null && (text.Any(char.IsControl) || Instruction().IsMatch(text));

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
