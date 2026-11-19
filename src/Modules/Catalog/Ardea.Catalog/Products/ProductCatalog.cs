using Ardea.Catalog.Contracts;
using Ardea.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Catalog.Products;

/// <summary>
/// The catalogue in effect as two indexes: the curated entries alone, and curated with learned ones (master plan 6.9).
/// Every lookup asks the curated index first and the full one only when it knows nothing, so a learned entry can add
/// what the curated catalogue lacks but never change what it says.
/// </summary>
internal sealed class CatalogLayers
{
    public CatalogLayers(IEnumerable<CatalogProduct> products, IReadOnlyDictionary<string, string>? learnedAliases = null)
    {
        var list = products.ToList();
        Curated = new CatalogIndex(list.Where(p => p.Origin == ProductOrigin.Curated));
        All = new CatalogIndex(list, learnedAliases);
    }

    /// <summary>The curated entries alone.</summary>
    public CatalogIndex Curated { get; }

    /// <summary>Curated and learned entries in effect, with the learned mappings of raw keys.</summary>
    public CatalogIndex All { get; }

    public CatalogProduct? FindByKey(string key) => All.FindByKey(key);

    public CatalogProduct? FindByCpe(CpePair pair) => Curated.FindByCpe(pair) ?? All.FindByCpe(pair);

    public CatalogProduct? FindByPackage(string type, string name) => Curated.FindByPackage(type, name) ?? All.FindByPackage(type, name);

    public CatalogProduct? FindByImage(string image) => Curated.FindByImage(image) ?? All.FindByImage(image);

    public ProductMatch? FindByName(string? vendor, string product) =>
        Curated.FindByName(vendor, product) ?? (All.FindByName(vendor, product) is { } learned ? learned with { Learned = true } : null);

    /// <summary>The curated catalogue by package, CPE pair and name first; learned entries only after it found nothing.</summary>
    public ResolvedProduct? Resolve(string? vendor, string product, CpePair? cpe, PackageCoordinate? package)
    {
        var raw = ProductKey.TryCreate(vendor, product, out var rawKey) ? rawKey : (ProductKey?)null;
        if (Lookup(Curated, vendor, product, cpe, package) is { } curated && ProductKey.TryParse(curated.Key, out var curatedKey))
        {
            return new ResolvedProduct(curatedKey, null);
        }

        if (Lookup(All, vendor, product, cpe, package) is { } learned && ProductKey.TryParse(learned.Key, out var learnedKey))
        {
            // Without a raw key (a name without letters, found by package or CPE pair) the learned key stands alone.
            return new ResolvedProduct(learnedKey, raw is { } r && r.Value != learnedKey.Value ? r.Value : null);
        }

        return raw is { } unknown ? new ResolvedProduct(unknown, null) : null;
    }

    private static CatalogProduct? Lookup(CatalogIndex index, string? vendor, string product, CpePair? cpe, PackageCoordinate? package) =>
        (package is { } p ? index.FindByPackage(p.Type, p.Name) : null)
        ?? (cpe is { } c ? index.FindByCpe(c) : null)
        ?? index.FindByName(vendor, product)?.Product;
}

/// <summary>
/// Holds the catalogue in memory for a few minutes: a few hundred curated entries plus the learned
/// ones change only with <c>migrate</c> or a decision about a learned entry, while lookups happen for
/// every affected product of every record.
/// </summary>
internal sealed class CatalogCache(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private (CatalogLayers Layers, DateTimeOffset LoadedAt, long Generation)? _current;

    /// <summary>
    /// The layers loaded for <paramref name="generation"/>: a copy loaded for another generation of decisions about
    /// learned entries is read again at once, whatever its age (ADR 040).
    /// </summary>
    public async ValueTask<CatalogLayers> GetAsync(long generation, Func<CancellationToken, Task<CatalogLayers>> load, CancellationToken cancellationToken)
    {
        var current = _current;
        if (current is { } c && c.Generation == generation && time.GetUtcNow() - c.LoadedAt < Lifetime)
        {
            return c.Layers;
        }

        var layers = await load(cancellationToken);
        _current = (layers, time.GetUtcNow(), generation);
        return layers;
    }

    /// <summary>The next lookup reads the database again.</summary>
    public void Invalidate() => _current = null;
}

/// <summary>Lookups in <c>catalog.product</c> inside the caller's unit of work (master plan 4.5, 6.5, 6.9).</summary>
internal sealed class ProductCatalog(CatalogDbContext context, CatalogCache cache) : IProductCatalog
{
    private long? _generation;

    public async Task<CatalogProduct?> FindByKeyAsync(string key, CancellationToken cancellationToken) =>
        (await LayersAsync(cancellationToken)).FindByKey(key);

    public async Task<ProductMatch?> FindByNameAsync(string? vendor, string product, CancellationToken cancellationToken) =>
        (await LayersAsync(cancellationToken)).FindByName(vendor, product);

    public async Task<CatalogProduct?> FindByCpeAsync(CpePair pair, CancellationToken cancellationToken) =>
        (await LayersAsync(cancellationToken)).FindByCpe(pair);

    public async Task<CatalogProduct?> FindByPackageAsync(string type, string name, CancellationToken cancellationToken) =>
        (await LayersAsync(cancellationToken)).FindByPackage(type, name);

    public async Task<CatalogProduct?> FindByImageAsync(string image, CancellationToken cancellationToken) =>
        (await LayersAsync(cancellationToken)).FindByImage(image);

    public async Task<ProductKey?> ResolveKeyAsync(string? vendor, string product, CpePair? cpe, PackageCoordinate? package, CancellationToken cancellationToken) =>
        (await ResolveAsync(vendor, product, cpe, package, cancellationToken))?.Key;

    public async Task<ResolvedProduct?> ResolveAsync(string? vendor, string product, CpePair? cpe, PackageCoordinate? package, CancellationToken cancellationToken) =>
        (await LayersAsync(cancellationToken)).Resolve(vendor, product, cpe, package);

    public async Task<IReadOnlyList<string>> LearnedFromAsync(string key, CancellationToken cancellationToken) =>
        await context.UnknownProducts.AsNoTracking()
            .Where(u => u.ProductKey == key
                && (u.Outcome == UnknownProductRow.New || u.Outcome == UnknownProductRow.Withdrawn
                    || (u.Outcome == UnknownProductRow.Alias && context.Products.Any(p => p.Key == key && p.Origin == ProductRow.Learned))))
            .OrderBy(u => u.RawKey)
            .Select(u => u.RawKey)
            .ToListAsync(cancellationToken);

    public async Task<bool> HasPluginsAsync(string platformKey, CancellationToken cancellationToken) =>
        (await LayersAsync(cancellationToken)).All.Products.Any(p => string.Equals(p.PlatformKey, platformKey, StringComparison.Ordinal));

    public async Task<IReadOnlyList<CatalogProduct>> PluginsOfAsync(string platformKey, CancellationToken cancellationToken) =>
        [.. (await LayersAsync(cancellationToken)).All.Products.Where(p => string.Equals(p.PlatformKey, platformKey, StringComparison.Ordinal)).OrderBy(p => p.Key, StringComparer.Ordinal)];

    public async Task<IReadOnlyList<CatalogProduct>> SearchAsync(string text, int limit, CancellationToken cancellationToken) =>
        (await LayersAsync(cancellationToken)).All.Search(text, limit);

    /// <summary>
    /// The catalogue in effect, from the cache. Once per unit of work the number of decisions about learned entries is
    /// read: a decision made in any process since the cache was loaded makes this unit of work read the catalogue anew,
    /// so a recomputation after a decision never works with the catalogue from before it (ADR 040).
    /// </summary>
    public async ValueTask<CatalogLayers> LayersAsync(CancellationToken cancellationToken)
    {
        _generation ??= await context.LearningEvents
            .LongCountAsync(e => e.Kind == LearningEventRow.Adopted || e.Kind == LearningEventRow.Confirmed || e.Kind == LearningEventRow.Withdrawn, cancellationToken);
        return await cache.GetAsync(_generation.Value, LoadAsync, cancellationToken);
    }

    /// <summary>The catalogue in effect as this unit of work sees it, past the cache: what a decision is checked against.</summary>
    public Task<CatalogLayers> ReadLayersAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    private async Task<CatalogLayers> LoadAsync(CancellationToken cancellationToken)
    {
        // In effect: every curated entry, learned ones only while active (master plan 6.9).
        var rows = await context.Products.AsNoTracking()
            .Where(p => p.Origin == ProductRow.Curated || p.State == ProductRow.Active)
            .ToListAsync(cancellationToken);
        // Raw keys mapped to a catalogue key: as alias of an entry, or as the source of a learned entry.
        var aliases = await context.UnknownProducts.AsNoTracking()
            .Where(u => (u.Outcome == UnknownProductRow.Alias || u.Outcome == UnknownProductRow.New) && u.ProductKey != null)
            .Select(u => new { u.RawKey, u.ProductKey })
            .ToDictionaryAsync(u => u.RawKey, u => u.ProductKey!, StringComparer.Ordinal, cancellationToken);
        return new CatalogLayers(rows.Select(ProductRows.ToProduct), aliases);
    }
}
