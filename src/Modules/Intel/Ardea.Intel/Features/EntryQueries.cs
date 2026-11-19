using System.Text.Json;
using Ardea.Intel.Contracts;
using Ardea.Intel.Persistence;
using Ardea.Intel.Sync;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Intel.Features;

/// <summary>Entries for the Assessment module (master plan 3.2, 7.2); global data in the caller's unit of work.</summary>
internal sealed class EntryQueries(IntelDbContext db) : IEntryQueries
{
    public async Task<EntryFacts?> FindAsync(Guid entryId, CancellationToken cancellationToken) =>
        (await FindManyAsync([entryId], cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<EntryFacts>> FindManyAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return [];
        }

        var ids = entryIds.Distinct().ToArray();
        var entries = await db.Entries.AsNoTracking().Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken);
        var affected = (await db.EntryAffected.AsNoTracking().Where(a => ids.Contains(a.EntryId)).ToListAsync(cancellationToken))
            .ToLookup(a => a.EntryId);
        return [.. entries.Select(e => Facts(e, affected[e.Id]))];
    }

    public async Task<IReadOnlyList<EntryText>> TextsAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return [];
        }

        var ids = entryIds.Distinct().ToArray();
        var entries = await db.Entries.AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.PrimaryKey, e.Title, e.Description, e.Details, e.Provenance }).ToListAsync(cancellationToken);
        var references = (await db.EntryReferences.AsNoTracking().Where(r => ids.Contains(r.EntryId)).OrderBy(r => r.Id).ToListAsync(cancellationToken))
            .ToLookup(r => r.EntryId);
        var sources = await db.Sources.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Name, StringComparer.Ordinal, cancellationToken);
        return [.. entries.Select(e =>
        {
            var details = e.Details?.RootElement;
            var refs = references[e.Id].ToList();
            var advisory = refs.Find(r => r.Tags.Contains("vendor-advisory")) ?? refs.Find(r => r.Tags.Contains("patch")) ?? refs.FirstOrDefault();
            var source = e.Provenance.RootElement.TryGetProperty("description", out var key) && key.ValueKind == JsonValueKind.String ? key.GetString() : null;
            return new EntryText(
                e.Id, e.PrimaryKey, e.Title, e.Description, source is null ? null : sources.GetValueOrDefault(source, source),
                Strings(details, "solutions"), Strings(details, "workarounds"), advisory?.Url)
            {
                References = [.. refs.DistinctBy(r => r.Url, StringComparer.Ordinal).Select(r => new EntryReference(r.Url, r.Tags))],
            };
        })];
    }

    private static IReadOnlyList<string> Strings(JsonElement? details, string name) =>
        details is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!).Where(t => t.Length > 0)]
            : [];

    public async Task<IReadOnlyList<Guid>> FindAffectingAsync(AffectedKeys keys, DateTimeOffset since, CancellationToken cancellationToken)
    {
        if (keys.IsEmpty)
        {
            return [];
        }

        // Three lookups, each on its own index of entry_affected (master plan 4.4), then the entry's state and age.
        var products = keys.ProductKeys.Distinct(StringComparer.Ordinal).ToArray();
        var cpeVendors = keys.CpePairs.Select(p => p.First).ToArray();
        var cpeProducts = keys.CpePairs.Select(p => p.Second).ToArray();
        var purlTypes = keys.Packages.Select(p => p.First).ToArray();
        var purlNames = keys.Packages.Select(p => p.Second).ToArray();
        return await db.Database.SqlQuery<Guid>($"""
            SELECT e.id AS "Value"
            FROM intel.security_entry e
            WHERE e.id IN (
                SELECT a.entry_id FROM intel.entry_affected a WHERE a.product_key = ANY ({products})
                UNION
                SELECT a.entry_id FROM intel.entry_affected a
                JOIN unnest({cpeVendors}::text[], {cpeProducts}::text[]) AS k(vendor, product) ON a.cpe_vendor = k.vendor AND a.cpe_product = k.product
                UNION
                SELECT a.entry_id FROM intel.entry_affected a
                JOIN unnest({purlTypes}::text[], {purlNames}::text[]) AS k(type, name) ON a.purl_type = k.type AND a.purl_name = k.name)
              AND e.kind = 'vulnerability' AND e.state IN ('published', 'disputed') AND e.changed_at >= {since}
            ORDER BY e.id
            """).ToListAsync(cancellationToken);
    }

    private static EntryFacts Facts(EntryRow entry, IEnumerable<EntryAffectedRow> affected)
    {
        var evidence = Evidence(entry.ExploitationEvidence)
            .Where(e => e.State == entry.Exploitation)
            .OrderBy(e => e.Since ?? DateTimeOffset.MaxValue)
            .FirstOrDefault();
        return new EntryFacts
        {
            Id = entry.Id,
            Key = entry.PrimaryKey,
            Kind = entry.Kind,
            State = entry.State,
            MergedIntoId = entry.MergedIntoId,
            Revision = entry.Revision,
            Title = entry.Title,
            PublishedAt = entry.PublishedAt,
            ChangedAt = entry.ChangedAt,
            CvssScore = entry.CvssScore,
            CvssVersion = entry.CvssVersion,
            CvssVector = entry.CvssVector,
            Exploitation = entry.Exploitation,
            ExploitationSource = entry.Exploitation == "none" ? null : evidence?.Source,
            ExploitationSince = entry.Exploitation == "none" ? null : evidence?.Since,
            EpssClass = entry.EpssClass,
            FixAvailable = entry.FixAvailable,
            AuthorityWarning = entry.AuthorityWarning,
            FixVersionsInText = entry.FixVersionsInText,
            Affected = [.. affected.OrderBy(a => a.Id).Select(a => new AffectedFacts
            {
                SourceId = a.SourceId,
                Vendor = a.Vendor,
                Product = a.Product,
                ProductKey = a.ProductKey,
                CpeVendor = a.CpeVendor,
                CpeProduct = a.CpeProduct,
                PurlType = a.PurlType,
                PurlName = a.PurlName,
                VersionScheme = a.VersionScheme,
                Introduced = a.Introduced,
                Fixed = a.Fixed,
                LastAffected = a.LastAffected,
                Exact = a.Exact,
                Status = a.Status,
                DefaultStatus = a.DefaultStatus,
            })],
        };
    }

    private static IEnumerable<Correlation.ExploitationEvidence> Evidence(JsonDocument? document)
    {
        if (document is null)
        {
            return [];
        }

        try
        {
            return document.Deserialize<List<Correlation.ExploitationEvidence>>(NormalizeRecordsHandler.Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
