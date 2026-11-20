using System.Text.Json;
using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Persistence;

/// <summary>Mapping between <see cref="CatalogProduct"/> and <see cref="ProductRow"/>, written out by hand.</summary>
internal static class ProductRows
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static CatalogProduct ToProduct(ProductRow row) => new()
    {
        Key = row.Key,
        Name = row.Name,
        Vendor = row.Vendor,
        Kind = Enum.Parse<ProductKind>(row.Kind, ignoreCase: true),
        CpePairs = [.. row.CpePairs.Select(p => CpePair.TryParse(p, out var pair) ? pair : (CpePair?)null).OfType<CpePair>()],
        Packages = [.. row.Purls.Select(p => PackageCoordinate.TryParsePurl(p, out var package, out _) ? package : null).OfType<PackageCoordinate>()],
        Aliases = ReadAliases(row.Aliases),
        VersionScheme = row.VersionScheme,
        PlatformKey = row.PlatformKey,
        RelatedKeys = row.RelatedKeys,
        ImageNames = row.ImageNames,
        Noisy = row.Noisy,
        Origin = row.Origin == ProductRow.Learned ? ProductOrigin.Learned : ProductOrigin.Curated,
        State = Enum.TryParse<ProductState>(row.State, ignoreCase: true, out var state) ? state : ProductState.Pending,
    };

    /// <summary>Writes the curated fields of a product into a row; only values that differ are assigned.</summary>
    public static void WriteCurated(CatalogProduct product, ProductRow row)
    {
        row.Name = product.Name;
        row.Vendor = product.Vendor;
        row.Kind = product.Kind.ToString().ToLowerInvariant();
        SetIfDifferent(row.CpePairs, [.. product.CpePairs.Select(p => p.ToString())], v => row.CpePairs = v);
        SetIfDifferent(row.Purls, [.. product.Packages.Select(p => p.ToString())], v => row.Purls = v);
        var aliases = WriteAliases(product.Aliases);
        if (WriteAliases(ReadAliases(row.Aliases)) != aliases)
        {
            // jsonb rewrites the text it stores; compare the content, not the text.
            row.Aliases = aliases;
        }

        row.VersionScheme = product.VersionScheme;
        row.PlatformKey = product.PlatformKey;
        SetIfDifferent(row.RelatedKeys, [.. product.RelatedKeys], v => row.RelatedKeys = v);
        SetIfDifferent(row.ImageNames, [.. product.ImageNames], v => row.ImageNames = v);
        row.Noisy = product.Noisy;
        row.Origin = ProductRow.Curated;
        row.State = ProductRow.Active;
    }

    private static IReadOnlyList<ProductAlias> ReadAliases(string json) =>
        JsonSerializer.Deserialize<List<ProductAlias>>(json, Json) ?? [];

    private static string WriteAliases(IReadOnlyList<ProductAlias> aliases) => JsonSerializer.Serialize(aliases, Json);

    private static void SetIfDifferent(string[] current, string[] wanted, Action<string[]> set)
    {
        if (!current.AsSpan().SequenceEqual(wanted))
        {
            set(wanted);
        }
    }
}
