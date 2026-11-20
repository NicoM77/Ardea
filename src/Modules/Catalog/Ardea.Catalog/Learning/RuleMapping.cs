using System.Text.RegularExpressions;
using Ardea.Catalog.Contracts;
using Ardea.Catalog.Products;

namespace Ardea.Catalog.Learning;

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

        var vendor = ProductKey.NormalizeVendor(statement.Vendor) is null ? null : statement.Vendor!.Trim();
        var cpe = statement.CpePairs.Select(ProductDrafts.CpeText).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        vendor ??= cpe.Count > 0 ? Words(cpe[0].Split(':')[0]) : null;
        var name = NameOf(vendor, statement.Product);
        if (cpe.Count > 0)
        {
            return New(statement, catalog, keys, name, vendor, "other", VersionSchemeNames.Numeric, cpe, package, "unknown CPE pair");
        }

        if (package is { } unknownPackage)
        {
            return New(statement, catalog, keys, name, vendor, "library", SchemeOf(unknownPackage.Type), [], unknownPackage, "unknown package");
        }

        return null;
    }

    public static string NameOf(string? vendor, string product)
    {
        var words = product.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && VersionWord().IsMatch(words[^1]))
        {
            words.RemoveAt(words.Count - 1);
        }

        var name = words.Count == 0 ? product.Trim() : string.Join(' ', words);
        var vendorName = VendorName(vendor);
        if (vendorName is null || ProductKey.NormalizeProduct(name)?.StartsWith(ProductKey.NormalizeVendor(vendorName) ?? "\0", StringComparison.Ordinal) == true)
        {
            return name;
        }

        var generic = words.All(w => GenericWords.Contains(w.ToLowerInvariant().Trim('.', ',', '-'))) || name.Length <= 3;
        return generic ? $"{vendorName} {name}" : name;
    }

    private static readonly HashSet<string> GenericWords = new(StringComparer.Ordinal)
    {
        "agent", "server", "client", "portal", "gateway", "insight", "insights", "manager", "management", "console", "studio",
        "cloud", "app", "application", "api", "core", "platform", "suite", "plugin", "connector", "controller", "dashboard",
        "monitor", "monitoring", "proxy", "router", "firmware", "library", "sdk", "cli", "tool", "tools", "service", "services",
        "center", "centre", "hub", "web", "desktop", "mobile", "enterprise", "community", "edition", "framework", "engine",
        "system", "module", "extension", "viewer", "editor", "player", "reader", "driver", "operator", "exporter", "daemon",
        "backup", "admin", "control", "panel", "online", "pro", "plus", "lite", "express", "standard",
    };

    private static string? VendorName(string? vendor)
    {
        if (ProductKey.NormalizeVendor(vendor) is null)
        {
            return null;
        }

        var words = vendor!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && LegalForms.Contains(words[^1].ToLowerInvariant().Trim('.', ',')))
        {
            words.RemoveAt(words.Count - 1);
        }

        return string.Join(' ', words).TrimEnd(',');
    }

    private static readonly HashSet<string> LegalForms = new(StringComparer.Ordinal)
    {
        "inc", "corp", "corporation", "co", "company", "ltd", "limited", "llc", "plc", "gmbh", "ag", "sa", "sas", "bv", "nv", "srl", "spa", "ab", "oy",
        "labs", "software", "foundation", "project", "team", "technologies", "systems", "group",
    };

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

    public static IReadOnlyList<ProductAlias> RecordSpelling(UnknownProductStatement statement, string name, string? vendor)
    {
        var product = statement.Product.Trim();
        var sameName = ProductKey.NormalizeProduct(product) == ProductKey.NormalizeProduct(name);
        var sameVendor = ProductKey.NormalizeVendor(statement.Vendor) == ProductKey.NormalizeVendor(vendor);
        return sameName && sameVendor || !ProductDrafts.Text(product, ProductDrafts.NameLength * 2)
            ? []
            : [new ProductAlias(ProductKey.NormalizeVendor(statement.Vendor) is null ? null : statement.Vendor!.Trim(), [product])];
    }

    private static string Words(string cpeVendor) => cpeVendor.Replace('_', ' ').Replace('\\', ' ').Trim();

    [GeneratedRegex(@"^(v(ersion)?\.?)?\d+([._-]\d+)*[a-z]?$|^(version|release|rel|v)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionWord();
}
