using Ardea.Catalog.Contracts;
using Ardea.Intel.Correlation;
using Ardea.Intel.Persistence;
using Ardea.Platform.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Intel.Features;

/// <summary>
/// The unknown products of the entries for the catalogue that learns (master plan 6.5, 6.9 step 1): every product key of
/// <c>entry_affected</c> the catalogue does not know, with the number of entries that name it, whether one of them is in
/// KEV, and the statement the records make most often. Public data of the sources only.
/// </summary>
internal sealed class UnknownProductFeed(IntelDbContext db, IProductCatalog catalog) : IUnknownProductFeed
{
    public async Task<UnknownProductCensus> CollectAsync(CancellationToken cancellationToken)
    {
        // Rows a learned entry resolved carry a key of the catalogue; the rest are catalogue keys or raw keys.
        var keys = await db.Database.SqlQuery<KeyRow>($"""
            SELECT a.product_key AS key,
                   count(DISTINCT a.entry_id)::int AS records,
                   coalesce(bool_or(e.provenance ->> 'exploitation' = 'cisa-kev'), false) AS in_kev,
                   mode() WITHIN GROUP (ORDER BY a.vendor) AS vendor,
                   mode() WITHIN GROUP (ORDER BY coalesce(a.product, a.purl_name)) AS product,
                   array_remove(array_agg(DISTINCT CASE WHEN a.cpe_vendor IS NOT NULL AND a.cpe_product IS NOT NULL
                                                        THEN a.cpe_vendor || ':' || a.cpe_product END), NULL) AS cpe_pairs,
                   min(CASE WHEN a.purl_type IS NOT NULL AND a.purl_name IS NOT NULL THEN 'pkg:' || a.purl_type || '/' || a.purl_name END) AS purl
            FROM intel.entry_affected a
            JOIN intel.security_entry e ON e.id = a.entry_id
            WHERE a.product_key IS NOT NULL AND a.learned_from IS NULL AND e.state <> 'merged'
            GROUP BY a.product_key
            """).ToListAsync(cancellationToken);

        var unknown = new List<UnknownProductStatement>();
        foreach (var key in keys)
        {
            if (key.Product is null || await catalog.FindByKeyAsync(key.Key, cancellationToken) is not null)
            {
                continue;
            }

            unknown.Add(new UnknownProductStatement(key.Key, key.Vendor, key.Product, [.. key.CpePairs.Order(StringComparer.Ordinal).Take(10)], key.Purl, key.Records, key.InKev));
        }

        return new UnknownProductCensus(keys.Count, unknown);
    }

    public async Task<IReadOnlyList<string>> TitlesAsync(string rawKey, int limit, CancellationToken cancellationToken) =>
        await db.Database.SqlQuery<string>($"""
            SELECT e.title AS "Value"
            FROM intel.security_entry e
            WHERE e.id IN (SELECT a.entry_id FROM intel.entry_affected a WHERE a.product_key = {rawKey})
              AND e.title IS NOT NULL AND e.state <> 'merged'
            ORDER BY e.published_at DESC NULLS LAST, e.id
            LIMIT {limit}
            """).ToListAsync(cancellationToken);

    private sealed record KeyRow(string Key, int Records, bool InKev, string? Vendor, string? Product, string[] CpePairs, string? Purl);
}

/// <summary>
/// Learned catalogue entries took effect or were withdrawn: the entries that name what they map are merged again (master
/// plan 6.9 step 6). Any recomputation of the entry serves, also one already waiting: every unit of work reads the
/// catalogue anew once a decision was made (ADR 040). New hits follow like any other change.
/// </summary>
internal sealed class LearnedEntriesSubscriber(IntelDbContext db, IJobQueue jobs) : ICatalogChangeSubscriber
{
    public async Task OnLearnedEntriesChangedAsync(LearnedEntriesChanged change, CancellationToken cancellationToken)
    {
        var keys = change.RawKeys.Concat(change.ProductKeys).Distinct(StringComparer.Ordinal).ToArray();
        var raw = change.RawKeys.ToArray();
        var pairs = change.CpePairs.Select(p => p.Split(':', 2)).Where(p => p.Length == 2).ToList();
        var cpeVendors = pairs.Select(p => p[0]).ToArray();
        var cpeProducts = pairs.Select(p => p[1]).ToArray();
        var packages = change.Packages.Select(p => PackageCoordinate.TryParsePurl(p, out var package, out _) ? package : null).OfType<PackageCoordinate>().ToList();
        var purlTypes = packages.Select(p => p.Type).ToArray();
        var purlNames = packages.Select(p => p.Name).ToArray();
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT a.entry_id AS "Value" FROM intel.entry_affected a WHERE a.product_key = ANY ({keys})
            UNION
            SELECT a.entry_id FROM intel.entry_affected a WHERE a.learned_from = ANY ({raw})
            UNION
            SELECT a.entry_id FROM intel.entry_affected a
            JOIN unnest({cpeVendors}::text[], {cpeProducts}::text[]) AS k(vendor, product) ON a.cpe_vendor = k.vendor AND a.cpe_product = k.product
            UNION
            SELECT a.entry_id FROM intel.entry_affected a
            JOIN unnest({purlTypes}::text[], {purlNames}::text[]) AS k(type, name) ON a.purl_type = k.type AND a.purl_name = k.name
            """).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return;
        }

        // The key a recomputation starts from, as «intel recompute» chooses it.
        var aliases = await db.EntryAliases.AsNoTracking()
            .Where(a => ids.Contains(a.EntryId))
            .GroupBy(a => a.EntryId).Select(g => g.Min(a => a.Scheme + ":" + a.Value)!)
            .ToListAsync(cancellationToken);
        var warnings = await db.Entries.AsNoTracking().Where(e => ids.Contains(e.Id) && e.Kind == "warning" && e.State != "merged")
            .Select(e => "SRC:" + e.PrimaryKey).ToListAsync(cancellationToken);
        foreach (var key in aliases.Concat(warnings).Distinct(StringComparer.Ordinal))
        {
            await jobs.EnqueueAsync(new CorrelateEntry(key), CorrelateEntry.Options(key), cancellationToken);
        }
    }
}
