using System.Text.RegularExpressions;
using Ardea.Catalog.Contracts;
using Ardea.Catalog.Products;

namespace Ardea.Catalog.Learning;

/// <summary>
/// The mapping without a model (master plan 6.9 step 2), which also runs without any provider:
/// <list type="number">
/// <item>a CPE pair or package an entry knows: alias of that entry;</item>
/// <item>the same name but for legal form, version suffix, a leading vendor name and spelling: alias of that entry;</item>
/// <item>a CPE pair no entry knows: a new entry with name and vendor from the record and the numeric version scheme;</item>
/// <item>a package no entry knows: a new entry of kind library with the ecosystem's version scheme (ADR 040).</item>
/// </list>
/// Everything else is left to the model, or to the administrator without one.
/// </summary>
internal static partial class RuleMapping
{
    public static LearnedProposal? Map(UnknownProductStatement statement, CatalogLayers catalog, ISet<string> keys)
    {
        foreach (var text in statement.CpePairs)
        {
            if (CpePair.TryParse(text, out var pair) && catalog.FindByCpe(pair) is { } byCpe)
            {
                return Alias(byCpe.Key, "same CPE pair");
            }
        }

        PackageCoordinate? package = PackageCoordinate.TryParsePurl(statement.Purl, out var coordinate, out _) ? coordinate : null;
        if (package is { } p && catalog.FindByPackage(p.Type, p.Name) is { } byPackage)
        {
            return Alias(byPackage.Key, "same package");
        }

        foreach (var variant in Variants(statement.Vendor, statement.Product))
        {
            if (catalog.FindByName(statement.Vendor, variant) is { } match)
            {
                return Alias(match.Product.Key, "same name but for version, vendor and spelling");
            }
        }

        var name = Variants(statement.Vendor, statement.Product).FirstOrDefault() ?? statement.Product.Trim();
        var vendor = ProductKey.NormalizeVendor(statement.Vendor) is null ? null : statement.Vendor!.Trim();
        var cpe = statement.CpePairs.Select(ProductDrafts.CpeText).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (cpe.Count > 0)
        {
            return New(statement, catalog, keys, name, vendor ?? Words(cpe[0].Split(':')[0]), "other", VersionSchemeNames.Numeric, cpe, package, "unknown CPE pair");
        }

        if (package is { } unknownPackage)
        {
            return New(statement, catalog, keys, name, vendor, "library", SchemeOf(unknownPackage.Type), [], unknownPackage, "unknown package");
        }

        return null;
    }

    /// <summary>
    /// The product name without trailing version numbers (<c>Widget 2.4</c>, <c>Widget v3</c>, <c>Widget version 5</c>) and
    /// without the vendor's name in front (<c>Acme Widget</c> of Acme); only forms that differ from the name itself.
    /// </summary>
    public static IEnumerable<string> Variants(string? vendor, string product)
    {
        var words = product.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && VersionWord().IsMatch(words[^1]))
        {
            words.RemoveAt(words.Count - 1);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal) { product.Trim() };
        var stripped = string.Join(' ', words);
        if (seen.Add(stripped))
        {
            yield return stripped;
        }

        // The vendor as its key compares it: «Microsoft Corporation» is «microsoft», so «Microsoft ASP.NET Core» is «ASP.NET Core».
        var vendorKey = ProductKey.NormalizeVendor(vendor);
        for (var take = 1; vendorKey is not null && take < words.Count; take++)
        {
            if (ProductKey.NormalizeProduct(string.Join(' ', words.Take(take))) == vendorKey)
            {
                var withoutVendor = string.Join(' ', words.Skip(take));
                if (seen.Add(withoutVendor))
                {
                    yield return withoutVendor;
                }

                break;
            }
        }
    }

    /// <summary>The version scheme of a package ecosystem.</summary>
    public static string SchemeOf(string purlType) => purlType switch
    {
        "npm" or "cargo" => VersionSchemeNames.SemVer,
        "golang" => VersionSchemeNames.Go,
        "composer" => VersionSchemeNames.Packagist,
        "nuget" => VersionSchemeNames.NuGet,
        "pypi" => VersionSchemeNames.Pep440,
        "maven" => VersionSchemeNames.Maven,
        "rpm" => VersionSchemeNames.Rpm,
        _ => VersionSchemeNames.Numeric,
    };

    private static LearnedProposal Alias(string key, string reason) =>
        new(Decisions.Alias, key, null, "high", reason, LearnedBy.Rule);

    private static LearnedProposal? New(
        UnknownProductStatement statement, CatalogLayers catalog, ISet<string> keys, string name, string? vendor, string kind, string scheme,
        IReadOnlyList<string> cpe, PackageCoordinate? package, string reason)
    {
        var key = ProductDrafts.KeyFor(name, vendor, k => keys.Contains(k) || catalog.FindByKey(k) is not null);
        if (key is null)
        {
            return null;
        }

        var product = new ProposedProduct(
            key, name, vendor, kind, scheme, cpe, package is { } p ? [p.ToString()] : [], RecordSpelling(statement, name, vendor), [], null);
        return ProductDrafts.Problem(product, catalog, keys, allowedCpe: null, allowedRelated: null) is null
            ? new LearnedProposal(Decisions.New, null, product, "high", reason, LearnedBy.Rule)
            : null;
    }

    /// <summary>The record's own spelling as alias, so that its vendor and product find the new entry.</summary>
    public static IReadOnlyList<ProductAlias> RecordSpelling(UnknownProductStatement statement, string name, string? vendor)
    {
        var product = statement.Product.Trim();
        var sameName = ProductKey.NormalizeProduct(product) == ProductKey.NormalizeProduct(name);
        var sameVendor = ProductKey.NormalizeVendor(statement.Vendor) == ProductKey.NormalizeVendor(vendor);
        return sameName && sameVendor || !ProductDrafts.Text(product, ProductDrafts.NameLength * 2)
            ? []
            : [new ProductAlias(ProductKey.NormalizeVendor(statement.Vendor) is null ? null : statement.Vendor!.Trim(), [product])];
    }

    /// <summary>A CPE vendor as words: <c>acme_corp</c> → <c>acme corp</c>.</summary>
    private static string Words(string cpeVendor) => cpeVendor.Replace('_', ' ').Replace('\\', ' ').Trim();

    [GeneratedRegex(@"^(v(ersion)?\.?)?\d+([._-]\d+)*[a-z]?$|^(version|release|rel|v)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionWord();
}
