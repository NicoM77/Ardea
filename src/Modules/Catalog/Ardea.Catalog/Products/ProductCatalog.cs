using Ardea.Catalog.Contracts;
using Ardea.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Catalog.Products;

/// <summary>
/// Holds the catalogue in memory for a few minutes: a few hundred curated entries plus the learned
/// ones change only with <c>migrate</c> or a decision about a learned entry, while lookups happen for
/// every affected product of every record.
/// </summary>
internal sealed class CatalogCache(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private (CatalogIndex Index, DateTimeOffset LoadedAt)? _current;

    public async ValueTask<CatalogIndex> GetAsync(Func<CancellationToken, Task<CatalogIndex>> load, CancellationToken cancellationToken)
    {
        var current = _current;
        if (current is { } c && time.GetUtcNow() - c.LoadedAt < Lifetime)
        {
            return c.Index;
        }

        var index = await load(cancellationToken);
        _current = (index, time.GetUtcNow());
        return index;
    }

    /// <summary>The next lookup reads the database again.</summary>
    public void Invalidate() => _current = null;
}

/// <summary>Lookups in <c>catalog.product</c> inside the caller's unit of work (master plan 4.5, 6.5, 6.9).</summary>
internal sealed class ProductCatalog(CatalogDbContext context, CatalogCache cache) : IProductCatalog
{
    public async Task<CatalogProduct?> FindByKeyAsync(string key, CancellationToken cancellationToken) =>
        (await IndexAsync(cancellationToken)).FindByKey(key);

    public async Task<ProductMatch?> FindByNameAsync(string? vendor, string product, CancellationToken cancellationToken) =>
        (await IndexAsync(cancellationToken)).FindByName(vendor, product);

    public async Task<CatalogProduct?> FindByCpeAsync(CpePair pair, CancellationToken cancellationToken) =>
        (await IndexAsync(cancellationToken)).FindByCpe(pair);

    public async Task<CatalogProduct?> FindByPackageAsync(string type, string name, CancellationToken cancellationToken) =>
        (await IndexAsync(cancellationToken)).FindByPackage(type, name);

    public async Task<CatalogProduct?> FindByImageAsync(string image, CancellationToken cancellationToken) =>
        (await IndexAsync(cancellationToken)).FindByImage(image);

    public async Task<ProductKey?> ResolveKeyAsync(string? vendor, string product, CpePair? cpe, PackageCoordinate? package, CancellationToken cancellationToken)
    {
        var index = await IndexAsync(cancellationToken);
        var known = (package is { } p ? index.FindByPackage(p.Type, p.Name) : null)
            ?? (cpe is { } c ? index.FindByCpe(c) : null)
            ?? index.FindByName(vendor, product)?.Product;
        if (known is not null && ProductKey.TryParse(known.Key, out var catalogKey))
        {
            return catalogKey;
        }

        return ProductKey.TryCreate(vendor, product, out var raw) ? raw : null;
    }

    public async Task<bool> HasPluginsAsync(string platformKey, CancellationToken cancellationToken) =>
        (await IndexAsync(cancellationToken)).Products.Any(p => string.Equals(p.PlatformKey, platformKey, StringComparison.Ordinal));

    public async Task<IReadOnlyList<CatalogProduct>> SearchAsync(string text, int limit, CancellationToken cancellationToken) =>
        (await IndexAsync(cancellationToken)).Search(text, limit);

    private ValueTask<CatalogIndex> IndexAsync(CancellationToken cancellationToken) => cache.GetAsync(LoadAsync, cancellationToken);

    private async Task<CatalogIndex> LoadAsync(CancellationToken cancellationToken)
    {
        // In effect: every curated entry, learned ones only while active (master plan 6.9).
        var rows = await context.Products.AsNoTracking()
            .Where(p => p.Origin == ProductRow.Curated || p.State == ProductRow.Active)
            .ToListAsync(cancellationToken);
        var aliases = await context.UnknownProducts.AsNoTracking()
            .Where(u => u.Outcome == UnknownProductRow.Alias && u.ProductKey != null)
            .Select(u => new { u.RawKey, u.ProductKey })
            .ToDictionaryAsync(u => u.RawKey, u => u.ProductKey!, StringComparer.Ordinal, cancellationToken);
        return new CatalogIndex(rows.Select(ProductRows.ToProduct), aliases);
    }
}
