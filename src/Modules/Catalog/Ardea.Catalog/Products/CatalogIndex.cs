using System.Text;
using System.Text.RegularExpressions;
using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Products;

/// <summary>
/// The lookups of the catalogue over entries in effect: by key, CPE pair, package, image and name
/// pattern. Curated entries are indexed first and win over learned ones (master plan 6.9). The same
/// index tells the validator which spellings, CPE pairs, packages or images two entries share.
/// </summary>
internal sealed class CatalogIndex
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(50);

    private readonly Dictionary<string, CatalogProduct> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<CpePair, CatalogProduct> _byCpe = [];
    private readonly Dictionary<PackageCoordinate, CatalogProduct> _byPackage = [];
    private readonly Dictionary<string, CatalogProduct> _byImage = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Vendor, string Name), List<CatalogProduct>> _byVendorAndName = [];
    private readonly Dictionary<string, List<CatalogProduct>> _byName = new(StringComparer.Ordinal);
    private readonly List<(string? Vendor, Regex Pattern, CatalogProduct Product)> _patterns = [];
    private readonly Dictionary<string, string> _learnedAliases;
    private readonly List<string> _conflicts = [];
    private readonly CatalogSearch _search;

    /// <param name="products">Entries in effect.</param>
    /// <param name="learnedAliases">Raw keys the instance has mapped to a catalogue key (<c>unknown_product</c> with outcome <c>alias</c>).</param>
    public CatalogIndex(IEnumerable<CatalogProduct> products, IReadOnlyDictionary<string, string>? learnedAliases = null)
    {
        _learnedAliases = new Dictionary<string, string>(learnedAliases ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        foreach (var product in products.OrderBy(p => p.Origin).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!_byKey.TryAdd(product.Key, product))
            {
                _conflicts.Add($"key '{product.Key}' is used twice");
                continue;
            }

            Products.Add(product);
            foreach (var pair in product.CpePairs)
            {
                AddUnique(_byCpe, pair, product, $"CPE pair '{pair}'");
                AddName(ProductKey.NormalizeVendor(pair.Vendor), pair.Product, product);
            }

            foreach (var package in product.Packages)
            {
                AddUnique(_byPackage, package, product, $"package '{package}'");
            }

            foreach (var image in product.ImageNames)
            {
                AddUnique(_byImage, NormalizeImage(image), product, $"image '{image}'");
            }

            AddName(ProductKey.NormalizeVendor(product.Vendor), product.Name, product);
            foreach (var alias in product.Aliases)
            {
                var vendor = ProductKey.NormalizeVendor(alias.Vendor);
                foreach (var name in alias.Names)
                {
                    if (IsPattern(name))
                    {
                        var regex = CompilePattern(name);
                        if (regex is null)
                        {
                            _conflicts.Add($"'{product.Key}': alias pattern '{name}' knows only the placeholders *, {{line}} and {{platform}}");
                        }
                        else
                        {
                            _patterns.Add((vendor, regex, product));
                        }
                    }
                    else
                    {
                        AddName(vendor, name, product);
                    }
                }
            }
        }

        _search = new CatalogSearch(Products);
    }

    /// <summary>Entries in effect, curated first.</summary>
    public List<CatalogProduct> Products { get; } = [];

    /// <summary>Spellings, CPE pairs, packages or images that two entries of the same origin share.</summary>
    public IReadOnlyList<string> Conflicts => _conflicts;

    public CatalogProduct? FindByKey(string key) => _byKey.GetValueOrDefault(key.Trim());

    public CatalogProduct? FindByCpe(CpePair pair) => _byCpe.GetValueOrDefault(pair);

    public CatalogProduct? FindByPackage(string type, string name) => _byPackage.GetValueOrDefault(PackageCoordinate.Normalize(type, name));

    public CatalogProduct? FindByImage(string image) => _byImage.GetValueOrDefault(NormalizeImage(image));

    public ProductMatch? FindByName(string? vendor, string product)
    {
        var vendorKey = ProductKey.NormalizeVendor(vendor);
        var nameKey = ProductKey.NormalizeProduct(product);
        var light = Light(product);

        ProductMatch? match = null;
        if (vendorKey is not null)
        {
            match = Pick(Candidates(nameKey is null ? null : _byVendorAndName.GetValueOrDefault((vendorKey, nameKey)), vendorKey, light));
        }

        // Names listed without vendor match whatever vendor a source gives.
        match ??= Pick(Candidates(nameKey is null ? null : _byVendorAndName.GetValueOrDefault((string.Empty, nameKey)), null, light));

        // Without a vendor, any spelling matches as long as only one product has it (master plan 6.5).
        if (vendorKey is null)
        {
            match ??= Pick(Candidates(nameKey is null ? null : _byName.GetValueOrDefault(nameKey), null, light, anyVendor: true));
        }

        if (match is null && ProductKey.TryCreate(vendor, product, out var raw) && _learnedAliases.TryGetValue(raw.Value, out var learned)
            && _byKey.TryGetValue(learned, out var target))
        {
            match = new ProductMatch(target);
        }

        return match;
    }

    /// <summary>
    /// Whether some entry already has this spelling, under any vendor: as name, alias, CPE product or through a name
    /// pattern. A learned entry may not take a spelling that is taken (master plan 6.9 step 4).
    /// </summary>
    public bool NameTaken(string name)
    {
        var nameKey = ProductKey.NormalizeProduct(name);
        if (nameKey is null)
        {
            return true;
        }

        if (_byName.ContainsKey(nameKey) || _byKey.ContainsKey(nameKey))
        {
            return true;
        }

        var light = Light(name);
        return _patterns.Exists(p => p.Pattern.IsMatch(light));
    }

    /// <summary>The raw keys the instance has mapped to a catalogue key.</summary>
    public IReadOnlyDictionary<string, string> LearnedAliases => _learnedAliases;

    /// <summary>Products for what someone types, best first (<see cref="CatalogSearch"/>).</summary>
    public IReadOnlyList<CatalogProduct> Search(string text, int limit) => _search.Search(text, limit);

    /// <summary>Image without registry <c>docker.io</c>, <c>library/</c>, tag and digest, in lower case.</summary>
    public static string NormalizeImage(string image)
    {
        var name = image.Trim().ToLowerInvariant();
        var at = name.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0)
        {
            name = name[..at];
        }

        var colon = name.LastIndexOf(':');
        if (colon > name.LastIndexOf('/'))
        {
            name = name[..colon];
        }

        foreach (var registry in new[] { "docker.io/", "index.docker.io/", "registry-1.docker.io/" })
        {
            if (name.StartsWith(registry, StringComparison.Ordinal))
            {
                name = name[registry.Length..];
            }
        }

        return name.StartsWith("library/", StringComparison.Ordinal) ? name["library/".Length..] : name;
    }

    public static bool IsPattern(string name) => name.Contains('*', StringComparison.Ordinal) || name.Contains('{', StringComparison.Ordinal);

    /// <summary>Lower case with single spaces: what alias patterns are matched against.</summary>
    public static string Light(string text) =>
        string.Join(' ', text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A pattern as an anchored expression; <c>null</c> for unknown placeholders.</summary>
    public static Regex? CompilePattern(string pattern)
    {
        var text = Light(pattern);
        var builder = new StringBuilder("^");
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '*')
            {
                builder.Append(".*?");
            }
            else if (text[i] == '{')
            {
                var end = text.IndexOf('}', i);
                var placeholder = end < 0 ? null : text[(i + 1)..end];
                switch (placeholder)
                {
                    case "line":
                        builder.Append(@"(?<line>\d+(?:\.\d+)*)");
                        break;
                    case "platform":
                        builder.Append("(?<platform>.+?)");
                        break;
                    default:
                        return null;
                }

                i = end;
            }
            else
            {
                builder.Append(Regex.Escape(text[i].ToString()));
            }
        }

        return new Regex(builder.Append('$').ToString(), RegexOptions.CultureInvariant, PatternTimeout);
    }

    private IEnumerable<ProductMatch> Candidates(List<CatalogProduct>? exact, string? vendorKey, string light, bool anyVendor = false)
    {
        foreach (var product in exact ?? [])
        {
            yield return new ProductMatch(product);
        }

        foreach (var (vendor, pattern, product) in _patterns)
        {
            if (!anyVendor && vendor != vendorKey)
            {
                continue;
            }

            var m = pattern.Match(light);
            if (m.Success)
            {
                yield return new ProductMatch(
                    product,
                    m.Groups["line"].Success ? m.Groups["line"].Value : null,
                    m.Groups["platform"].Success ? m.Groups["platform"].Value : null);
            }
        }
    }

    /// <summary>The one product the candidates name, curated first; <c>null</c> when none or ambiguous.</summary>
    private static ProductMatch? Pick(IEnumerable<ProductMatch> candidates)
    {
        var list = candidates.ToList();
        foreach (var origin in new[] { ProductOrigin.Curated, ProductOrigin.Learned })
        {
            var ofOrigin = list.Where(c => c.Product.Origin == origin).ToList();
            if (ofOrigin.Count == 0)
            {
                continue;
            }

            return ofOrigin.Select(c => c.Product.Key).Distinct(StringComparer.Ordinal).Count() == 1
                ? ofOrigin.OrderByDescending(c => c.VersionLine is not null).First()
                : null;
        }

        return null;
    }

    private void AddName(string? vendorKey, string name, CatalogProduct product)
    {
        var nameKey = ProductKey.NormalizeProduct(name);
        if (nameKey is null)
        {
            return;
        }

        var key = (vendorKey ?? string.Empty, nameKey);
        if (!_byVendorAndName.TryGetValue(key, out var list))
        {
            _byVendorAndName[key] = list = [];
        }

        if (list.Exists(p => p.Key == product.Key))
        {
            return;
        }

        if (list.Exists(p => p.Origin == product.Origin))
        {
            _conflicts.Add($"spelling '{name}'{(vendorKey is null ? string.Empty : $" of vendor '{vendorKey}'")} names '{list[0].Key}' and '{product.Key}'");
        }

        list.Add(product);

        // A learned single word under a vendor («Agent» of Grafana) finds the entry only with that vendor: without one it
        // is too generic to stand for the product (ADR 040).
        if (product.Origin == ProductOrigin.Learned && vendorKey is not null && !Light(name).Contains(' ', StringComparison.Ordinal))
        {
            return;
        }

        if (!_byName.TryGetValue(nameKey, out var byName))
        {
            _byName[nameKey] = byName = [];
        }

        if (!byName.Exists(p => p.Key == product.Key))
        {
            byName.Add(product);
        }
    }

    private void AddUnique<TKey>(Dictionary<TKey, CatalogProduct> index, TKey key, CatalogProduct product, string what)
        where TKey : notnull
    {
        if (index.TryGetValue(key, out var existing))
        {
            if (existing.Origin == product.Origin && existing.Key != product.Key)
            {
                _conflicts.Add($"{what} belongs to '{existing.Key}' and '{product.Key}'");
            }

            return;
        }

        index[key] = product;
    }
}
