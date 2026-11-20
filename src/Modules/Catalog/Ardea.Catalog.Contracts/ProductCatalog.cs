namespace Ardea.Catalog.Contracts;

public enum ProductKind
{
    Os,
    Runtime,
    Framework,
    Cms,
    Database,
    Messaging,
    Container,
    Network,
    Virtualization,
    Library,
    Plugin,
    Service,
    Other,
}

public enum ProductOrigin
{
    Curated,

    Learned,
}

public enum ProductState
{
    Active,
    Pending,
    Rejected,
    Withdrawn,
}

/// <summary>
/// Name patterns of a product under one spelling of its vendor (<c>aliases</c>, master plan 6.5).
/// A pattern without placeholders matches every spelling with the same product key; <c>*</c> stands
/// for any text, <c>{line}</c> captures a version line, <c>{platform}</c> a platform, e.g.
/// <c>.NET {line} installed on {platform}</c>. Without <see cref="Vendor"/>, the names match whatever
/// vendor a source gives, so they must be distinctive.
/// </summary>
public sealed record ProductAlias(string? Vendor, IReadOnlyList<string> Names);

public sealed record PackageCoordinate(string Type, string Name)
{
    public static bool TryParsePurl(string? purl, out PackageCoordinate coordinate, out string? version)
    {
        coordinate = new PackageCoordinate(string.Empty, string.Empty);
        version = null;
        var text = purl?.Trim();
        if (text is null || !text.StartsWith("pkg:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        text = text[4..].TrimStart('/');
        var cut = text.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            text = text[..cut];
        }

        var slash = text.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == text.Length - 1)
        {
            return false;
        }

        var type = text[..slash];
        var path = text[(slash + 1)..].TrimEnd('/');
        var at = path.LastIndexOf('@');
        if (at > path.LastIndexOf('/') && at > 0)
        {
            version = Uri.UnescapeDataString(path[(at + 1)..]);
            path = path[..at];
        }

        var name = string.Join('/', path.Split('/').Select(Uri.UnescapeDataString));
        coordinate = Normalize(type, name);
        return coordinate.Name.Length > 0;
    }

    public static PackageCoordinate Normalize(string type, string name)
    {
        var t = type.Trim().ToLowerInvariant() switch
        {
            "go" => "golang",
            "packagist" => "composer",
            "crates.io" => "cargo",
            "rubygems" => "gem",
            var other => other,
        };
        var n = name.Trim().ToLowerInvariant();
        n = t switch
        {
            "maven" => n.Replace(':', '/'),
            "pypi" => string.Join('-', n.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)),
            _ => n,
        };
        return new PackageCoordinate(t, n);
    }

    public override string ToString() => $"pkg:{Type}/{Name}";
}

public sealed record CatalogProduct
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public string? Vendor { get; init; }

    public required ProductKind Kind { get; init; }

    public IReadOnlyList<CpePair> CpePairs { get; init; } = [];

    public IReadOnlyList<PackageCoordinate> Packages { get; init; } = [];

    public IReadOnlyList<ProductAlias> Aliases { get; init; } = [];

    public required string VersionScheme { get; init; }

    public string? PlatformKey { get; init; }

    public IReadOnlyList<string> RelatedKeys { get; init; } = [];

    public IReadOnlyList<string> ImageNames { get; init; } = [];

    public bool Noisy { get; init; }

    public ProductOrigin Origin { get; init; } = ProductOrigin.Curated;

    public ProductState State { get; init; } = ProductState.Active;
}

public sealed record ProductMatch(CatalogProduct Product, string? VersionLine = null, string? Platform = null, bool Learned = false);

public sealed record ResolvedProduct(ProductKey Key, string? LearnedFrom);

public interface IProductCatalog
{
    Task<CatalogProduct?> FindByKeyAsync(string key, CancellationToken cancellationToken);

    Task<ProductMatch?> FindByNameAsync(string? vendor, string product, CancellationToken cancellationToken);

    Task<CatalogProduct?> FindByCpeAsync(CpePair pair, CancellationToken cancellationToken);

    Task<CatalogProduct?> FindByPackageAsync(string type, string name, CancellationToken cancellationToken);

    Task<CatalogProduct?> FindByImageAsync(string image, CancellationToken cancellationToken);

    Task<ProductKey?> ResolveKeyAsync(string? vendor, string product, CpePair? cpe, PackageCoordinate? package, CancellationToken cancellationToken);

    Task<ResolvedProduct?> ResolveAsync(string? vendor, string product, CpePair? cpe, PackageCoordinate? package, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> LearnedFromAsync(string key, CancellationToken cancellationToken);

    Task<bool> HasPluginsAsync(string platformKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogProduct>> PluginsOfAsync(string platformKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogProduct>> SearchAsync(string text, int limit, CancellationToken cancellationToken);
}
