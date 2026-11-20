using System.Text.Json;
using System.Text.Json.Serialization;
using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Products;

internal static class CatalogFiles
{
    public const string Folder = "catalog";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static CatalogLoad Load(string directory)
    {
        var products = new List<CatalogProduct>();
        var errors = new List<string>();
        if (!Directory.Exists(directory))
        {
            errors.Add($"The catalogue folder '{directory}' does not exist.");
            return new CatalogLoad(products, errors);
        }

        var files = Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal).ToList();
        if (files.Count == 0)
        {
            errors.Add($"The catalogue folder '{directory}' holds no catalogue file.");
        }

        foreach (var path in files)
        {
            var file = Path.GetFileName(path);
            CatalogFile? content;
            try
            {
                content = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path), Options);
            }
            catch (JsonException ex)
            {
                errors.Add($"{file}: {ex.Message}");
                continue;
            }

            if (content?.Products is not { Count: > 0 })
            {
                errors.Add($"{file}: lists no products.");
                continue;
            }

            foreach (var entry in content.Products)
            {
                var product = Map(entry, content.Vendor, $"{file}, {entry.Key ?? "?"}", errors);
                if (product is not null)
                {
                    products.Add(product);
                }
            }
        }

        return new CatalogLoad(products, errors);
    }

    private static CatalogProduct? Map(ProductEntry entry, string? fileVendor, string where, List<string> errors)
    {
        var before = errors.Count;
        if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.VersionScheme))
        {
            errors.Add($"{where}: key, name and versionScheme are required.");
        }

        if (!Enum.TryParse<ProductKind>(entry.Kind, ignoreCase: true, out var kind) || int.TryParse(entry.Kind, out _))
        {
            errors.Add($"{where}: unknown kind '{entry.Kind}'.");
        }

        var cpe = new List<CpePair>();
        foreach (var text in entry.Cpe ?? [])
        {
            if (CpePair.TryParse(text, out var pair))
            {
                cpe.Add(pair);
            }
            else
            {
                errors.Add($"{where}: '{text}' is not a CPE vendor:product pair.");
            }
        }

        var packages = new List<PackageCoordinate>();
        foreach (var text in entry.Packages ?? [])
        {
            if (PackageCoordinate.TryParsePurl(text, out var package, out var version) && version is null)
            {
                packages.Add(package);
            }
            else
            {
                errors.Add($"{where}: '{text}' is not a package URL without version.");
            }
        }

        if (errors.Count > before)
        {
            return null;
        }

        return new CatalogProduct
        {
            Key = entry.Key!.Trim(),
            Name = entry.Name!.Trim(),
            Vendor = entry.Vendor ?? fileVendor,
            Kind = kind,
            VersionScheme = entry.VersionScheme!.Trim(),
            CpePairs = cpe,
            Packages = packages,
            Aliases = [.. (entry.Aliases ?? []).Select(a => new ProductAlias(a.Vendor, a.Names ?? []))],
            PlatformKey = entry.Platform,
            RelatedKeys = entry.Related ?? [],
            ImageNames = entry.Images ?? [],
            Noisy = entry.Noisy,
        };
    }

    private sealed class CatalogFile
    {
        [JsonPropertyName("$schema")]
        public string? Schema { get; set; }

        public string? Vendor { get; set; }

        public List<ProductEntry>? Products { get; set; }
    }

    private sealed class ProductEntry
    {
        public string? Key { get; set; }

        public string? Name { get; set; }

        public string? Vendor { get; set; }

        public string? Kind { get; set; }

        public string? VersionScheme { get; set; }

        public List<string>? Cpe { get; set; }

        public List<string>? Packages { get; set; }

        public List<AliasEntry>? Aliases { get; set; }

        public string? Platform { get; set; }

        public List<string>? Related { get; set; }

        public List<string>? Images { get; set; }

        public bool Noisy { get; set; }
    }

    private sealed class AliasEntry
    {
        public string? Vendor { get; set; }

        public List<string>? Names { get; set; }
    }
}

internal sealed record CatalogLoad(IReadOnlyList<CatalogProduct> Products, IReadOnlyList<string> Errors);
