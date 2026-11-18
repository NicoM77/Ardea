using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ardea.Intel.Persistence;
using Ardea.Intel.Sources.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Intel.Sync;

/// <summary>Counts of one stored batch.</summary>
internal sealed record StoreResult(int Fetched, int Changed, IReadOnlyList<Guid> ChangedRecordIds);

/// <summary>
/// Raw record storage with revisions (master plan 4.4, 6.4 step 2). A record is identified by source
/// and external id; its canonical content is hashed, and an unchanged record only refreshes
/// <c>last_seen_at</c>. Every new state is kept as a revision; the first and the newest ten remain.
/// </summary>
internal static class RawRecordStore
{
    public const int KeptRevisions = 10;

    public static async Task<StoreResult> StoreAsync(IntelDbContext db, Guid sourceId, IReadOnlyList<RawRecord> records, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The same id twice in one batch: the later state wins, as it would over two runs.
        var latest = records.GroupBy(r => r.ExternalId, StringComparer.Ordinal).Select(g => g.Last()).ToList();
        var ids = latest.Select(r => r.ExternalId).ToList();
        var existing = await db.SourceRecords
            .Where(r => r.SourceId == sourceId && ids.Contains(r.ExternalId))
            .ToDictionaryAsync(r => r.ExternalId, StringComparer.Ordinal, cancellationToken);

        var changed = new List<Guid>();
        foreach (var record in latest)
        {
            var hash = Hash(record);
            if (existing.TryGetValue(record.ExternalId, out var row))
            {
                row.LastSeenAt = now;
                if (row.ContentHash == hash)
                {
                    continue;
                }

                row.Revision++;
                row.ContentHash = hash;
                row.Payload = record.Content;
                row.ContentType = record.ContentType;
                row.Url = record.Url?.AbsoluteUri ?? row.Url;
                row.SourcePublishedAt = record.PublishedAt ?? row.SourcePublishedAt;
                row.SourceModifiedAt = record.ModifiedAt ?? row.SourceModifiedAt;
                row.ParseStatus = "pending";
            }
            else
            {
                row = new SourceRecordRow
                {
                    Id = Guid.CreateVersion7(),
                    SourceId = sourceId,
                    ExternalId = record.ExternalId,
                    Url = record.Url?.AbsoluteUri,
                    ContentType = record.ContentType,
                    ContentHash = hash,
                    Payload = record.Content,
                    Revision = 1,
                    SourcePublishedAt = record.PublishedAt,
                    SourceModifiedAt = record.ModifiedAt,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    ParseStatus = "pending",
                };
                db.SourceRecords.Add(row);
            }

            db.SourceRecordRevisions.Add(new SourceRecordRevisionRow
            {
                Id = Guid.CreateVersion7(),
                SourceRecordId = row.Id,
                Revision = row.Revision,
                ContentHash = hash,
                Payload = record.Content,
                FetchedAt = now,
            });
            changed.Add(row.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (changed.Count > 0)
        {
            // Keep the first and the newest ten states of every changed record (master plan 4.10).
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM intel.source_record_revision v USING intel.source_record r
                WHERE v.source_record_id = r.id AND r.id = ANY({changed.ToArray()})
                  AND v.revision > 1 AND v.revision <= r.revision - {KeptRevisions}
                """,
                cancellationToken);
        }

        return new StoreResult(latest.Count, changed.Count, changed);
    }

    /// <summary>
    /// SHA-256 of the canonical content: JSON with sorted object keys and no insignificant whitespace,
    /// so a source that reorders or reformats an unchanged record does not create a revision.
    /// </summary>
    public static string Hash(RawRecord record)
    {
        var canonical = record.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase) ? CanonicalJson(record.Content) : record.Content;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static string CanonicalJson(string json)
    {
        try
        {
            return Canonical(JsonNode.Parse(json))?.ToJsonString() ?? "null";
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static JsonNode? Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Canonical(p.Value)))),
        JsonArray array => new JsonArray([.. array.Select(Canonical)]),
        _ => node?.DeepClone(),
    };
}
