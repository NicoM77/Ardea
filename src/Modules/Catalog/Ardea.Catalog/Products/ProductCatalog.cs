using Ardea.Catalog.Contracts;
using Ardea.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Catalog.Products;

internal sealed class CatalogLayers
{
    public CatalogLayers(IEnumerable<CatalogProduct> products, IReadOnlyDictionary<string, string>? learnedAliases = null)
    {
        var list = products.ToList();
        Curated = new CatalogIndex(list.Where(p => p.Origin == ProductOrigin.Curated));
        All = new CatalogIndex(list, learnedAliases);
    }

    public CatalogIndex Curated { get; }

    public CatalogIndex All { get; }

    public CatalogProduct? FindByKey(string key) => All.FindByKey(key);

    public CatalogProduct? FindByCpe(CpePair pair) => Curated.FindByCpe(pair) ?? All.FindByCpe(pair);

    public CatalogProduct? FindByPackage(string type, string name) => Curated.FindByPackage(type, name) ?? All.FindByPackage(type, name);

    public CatalogProduct? FindByImage(string image) => Curated.FindByImage(image) ?? All.FindByImage(image);

    public ProductMatch? FindByName(string? vendor, string product) =>
        Curated.FindByName(vendor, product) ?? (All.FindByName(vendor, product) is { } learned ? learned with { Learned = true } : null);

    public ResolvedProduct? Resolve(string? vendor, string product, CpePair? cpe, PackageCoordinate? package)
    {
        var raw = ProductKey.TryCreate(vendor, product, out var rawKey) ? rawKey : (ProductKey?)null;
        if (Lookup(Curated, vendor, product, cpe, package) is { } curated && ProductKey.TryParse(curated.Key, out var curatedKey))
        {
            return new ResolvedProduct(curatedKey, null);
        }

        if (Lookup(All, vendor, product, cpe, package) is { } learned && ProductKey.TryParse(learned.Key, out var learnedKey))
        {
            return new ResolvedProduct(learnedKey, raw is { } r && r.Value != learnedKey.Value ? r.Value : null);
        }

        return raw is { } unknown ? new ResolvedProduct(unknown, null) : null;
    }

    private static CatalogProduct? Lookup(CatalogIndex index, string? vendor, string product, CpePair? cpe, PackageCoordinate? package) =>
        (package is { } p ? index.FindByPackage(p.Type, p.Name) : null)
        ?? (cpe is { } c ? index.FindByCpe(c) : null)
        ?? index.FindByName(vendor, product)?.Product;
}

internal sealed class CatalogCache(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private (CatalogLayers Layers, DateTimeOffset LoadedAt, long Generation)? _current;

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

    public void Invalidate() => _current = null;
}

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

    public async ValueTask<CatalogLayers> LayersAsync(CancellationToken cancellationToken)
    {
        _generation ??= await context.LearningEvents
            .LongCountAsync(e => e.Kind == LearningEventRow.Adopted || e.Kind == LearningEventRow.Confirmed || e.Kind == LearningEventRow.Withdrawn, cancellationToken);
        return await cache.GetAsync(_generation.Value, LoadAsync, cancellationToken);
    }

    public Task<CatalogLayers> ReadLayersAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    private async Task<CatalogLayers> LoadAsync(CancellationToken cancellationToken)
    {
        var rows = await context.Products.AsNoTracking()
            .Where(p => p.Origin == ProductRow.Curated || p.State == ProductRow.Active)
            .ToListAsync(cancellationToken);
        var aliases = await context.UnknownProducts.AsNoTracking()
            .Where(u => (u.Outcome == UnknownProductRow.Alias || u.Outcome == UnknownProductRow.New) && u.ProductKey != null)
            .Select(u => new { u.RawKey, u.ProductKey })
            .ToDictionaryAsync(u => u.RawKey, u => u.ProductKey!, StringComparer.Ordinal, cancellationToken);
        return new CatalogLayers(rows.Select(ProductRows.ToProduct), aliases);
    }
}
