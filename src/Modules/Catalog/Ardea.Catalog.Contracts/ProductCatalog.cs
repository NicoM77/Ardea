namespace Ardea.Catalog.Contracts;

/// <summary>Kind of a product; the same list as <c>assets.component.kind</c> (master plan 4.5).</summary>
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

/// <summary>Where a catalogue entry comes from (master plan 6.9).</summary>
public enum ProductOrigin
{
    /// <summary>Curated in <c>data/catalog/</c> and shipped with the image; always in effect.</summary>
    Curated,

    /// <summary>Learned in the instance; in effect only while <see cref="ProductState.Active"/>.</summary>
    Learned,
}

/// <summary>State of a catalogue entry; curated entries are always active.</summary>
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

/// <summary>A package a product is distributed as: purl type and name with namespace, e.g. <c>maven</c>, <c>org.apache.logging.log4j/log4j-core</c>.</summary>
public sealed record PackageCoordinate(string Type, string Name)
{
    /// <summary>
    /// Reads a package URL (<c>pkg:npm/%40angular/core@18.0.0</c>) into type and name; version,
    /// qualifiers and subpath are dropped. Names are normalised as in <see cref="Normalize"/>.
    /// </summary>
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

    /// <summary>
    /// The comparable form: lower case; Maven's <c>group:artifact</c> as <c>group/artifact</c>;
    /// PyPI names per PEP 503; OSV ecosystem names as purl types (<c>Go</c> → <c>golang</c>,
    /// <c>Packagist</c> → <c>composer</c>).
    /// </summary>
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

    /// <summary>The package URL without version, e.g. <c>pkg:npm/@angular/core</c>.</summary>
    public override string ToString() => $"pkg:{Type}/{Name}";
}

/// <summary>A product of the catalogue (<c>catalog.product</c>).</summary>
public sealed record CatalogProduct
{
    /// <summary>The key components and entries refer to, e.g. <c>postgresql</c>.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    public string? Vendor { get; init; }

    public required ProductKind Kind { get; init; }

    public IReadOnlyList<CpePair> CpePairs { get; init; } = [];

    public IReadOnlyList<PackageCoordinate> Packages { get; init; } = [];

    public IReadOnlyList<ProductAlias> Aliases { get; init; } = [];

    /// <summary>One of <see cref="VersionSchemeNames"/>.</summary>
    public required string VersionScheme { get; init; }

    /// <summary>For plugins: the key of the platform they run on.</summary>
    public string? PlatformKey { get; init; }

    /// <summary>Keys of related, independent products.</summary>
    public IReadOnlyList<string> RelatedKeys { get; init; } = [];

    /// <summary>Container images the product is recognised from, e.g. <c>postgres</c>, <c>mcr.microsoft.com/dotnet/aspnet</c>.</summary>
    public IReadOnlyList<string> ImageNames { get; init; } = [];

    public ProductOrigin Origin { get; init; } = ProductOrigin.Curated;

    public ProductState State { get; init; } = ProductState.Active;
}

/// <summary>How a product was found by name.</summary>
public sealed record ProductMatch(CatalogProduct Product, string? VersionLine = null, string? Platform = null);

/// <summary>
/// The technology catalogue (master plan 4.5, 6.5): global data every role may read. Only entries in
/// effect take part: every curated entry, learned entries only while active (master plan 6.9).
/// When a curated and a learned entry would both match, the curated one wins.
/// </summary>
public interface IProductCatalog
{
    Task<CatalogProduct?> FindByKeyAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// The product a source means by vendor and product name, through the alias patterns. Without a
    /// usable vendor (missing, <c>n/a</c>) a name matches only when exactly one product has it.
    /// </summary>
    Task<ProductMatch?> FindByNameAsync(string? vendor, string product, CancellationToken cancellationToken);

    Task<CatalogProduct?> FindByCpeAsync(CpePair pair, CancellationToken cancellationToken);

    /// <summary>The product distributed as this package; type and name as in <see cref="PackageCoordinate.Normalize"/>.</summary>
    Task<CatalogProduct?> FindByPackageAsync(string type, string name, CancellationToken cancellationToken);

    /// <summary>The product a container image stands for; registry <c>docker.io</c>, <c>library/</c>, tag and digest are ignored.</summary>
    Task<CatalogProduct?> FindByImageAsync(string image, CancellationToken cancellationToken);

    /// <summary>
    /// The key an affected product of a source gets (master plan 6.5): the catalogue key when the
    /// catalogue knows the product by CPE pair, package or name, otherwise the raw <see cref="ProductKey"/>;
    /// <c>null</c> when nothing matches and the name has no letter or digit.
    /// </summary>
    Task<ProductKey?> ResolveKeyAsync(string? vendor, string product, CpePair? cpe, PackageCoordinate? package, CancellationToken cancellationToken);

    /// <summary>Whether some product in effect names <paramref name="platformKey"/> as its platform, i.e. the platform has plugins.</summary>
    Task<bool> HasPluginsAsync(string platformKey, CancellationToken cancellationToken);

    /// <summary>
    /// Products for what someone types into the input help, best match first: spaces, punctuation, case,
    /// trailing version numbers and small typos do not matter (<c>Postgre SQL</c>, <c>.net 8</c>, <c>kubernets</c>).
    /// </summary>
    Task<IReadOnlyList<CatalogProduct>> SearchAsync(string text, int limit, CancellationToken cancellationToken);
}
