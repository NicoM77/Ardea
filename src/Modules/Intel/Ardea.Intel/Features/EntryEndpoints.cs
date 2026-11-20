using System.Globalization;
using System.Text;
using System.Text.Json;
using Ardea.Intel.Correlation;
using Ardea.Intel.Persistence;
using Ardea.Intel.Sync;
using Ardea.Platform.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Intel.Features;

internal static class EntryEndpoints
{
    private const int MaxLimit = 100;

    public static void Map(IEndpointRouteBuilder api)
    {
        var entries = api.MapGroup("/entries").WithTags("Entries").RequireAuthorization();
        entries.MapGet("", ListAsync).WithName("listEntries").Produces<Page<EntrySummary>>().Problems(400);
        entries.MapGet("/{key}", GetAsync).WithName("getEntry").Produces<EntryDetail>().Problems(404);
        entries.MapGet("/{key}/changes", ChangesAsync).WithName("listEntryChanges").Produces<IReadOnlyList<EntryChangeView>>().Problems(404);
        entries.MapGet("/{key}/sources", SourcesAsync).WithName("listEntrySources").Produces<IReadOnlyList<EntrySourceView>>().Problems(404);
    }

    private static async Task<IResult> ListAsync(
        IntelDbContext db, string? q, DateTimeOffset? changedSince, string? kind, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(limit ?? 50, 1, MaxLimit);
        IQueryable<EntryRow> query;
        if (string.IsNullOrWhiteSpace(q))
        {
            query = db.Entries.AsNoTracking();
        }
        else if (LooksLikeIdentifier(q.Trim()))
        {
            var value = q.Trim().ToUpperInvariant();
            var ids = db.EntryAliases.Where(a => a.Value.ToUpper() == value).Select(a => a.EntryId);
            query = db.Entries.AsNoTracking().Where(e => ids.Contains(e.Id) || e.PrimaryKey.ToUpper() == value);
        }
        else
        {
            var text = q.Trim();
            query = db.Entries.FromSqlInterpolated($"SELECT * FROM intel.security_entry WHERE search @@ websearch_to_tsquery('english', {text})").AsNoTracking();
        }

        query = query.Where(e => e.State != "merged");
        if (changedSince is { } since)
        {
            query = query.Where(e => e.ChangedAt >= since);
        }

        if (kind is "vulnerability" or "warning")
        {
            query = query.Where(e => e.Kind == kind);
        }
        else if (kind is not null)
        {
            return ApiProblems.Validation("validation_failed", "Unknown kind.", new Dictionary<string, string[]> { ["kind"] = ["Use vulnerability or warning."] });
        }

        if (cursor is not null)
        {
            if (!TryReadCursor(cursor, out var changedAt, out var id))
            {
                return ApiProblems.Validation("invalid_cursor", "The cursor is not valid.", new Dictionary<string, string[]> { ["cursor"] = ["Start again from the first page."] });
            }

            query = query.Where(e => e.ChangedAt < changedAt || (e.ChangedAt == changedAt && e.Id.CompareTo(id) < 0));
        }

        var rows = await query.OrderByDescending(e => e.ChangedAt).ThenByDescending(e => e.Id).Take(size + 1).ToListAsync(cancellationToken);
        var page = rows.Take(size).ToList();
        var aliases = await AliasesAsync(db, page.Select(e => e.Id).ToList(), cancellationToken);
        var next = rows.Count > size ? WriteCursor(page[^1]) : null;
        return Results.Ok(new Page<EntrySummary>([.. page.Select(e => Summary(e, aliases.GetValueOrDefault(e.Id) ?? []))], next));
    }

    private static async Task<IResult> GetAsync(string key, IntelDbContext db, CancellationToken cancellationToken)
    {
        var (entry, redirectedFrom) = await ResolveAsync(db, key, cancellationToken);
        if (entry is null)
        {
            return NotFound(key);
        }

        var aliases = await AliasesAsync(db, [entry.Id], cancellationToken);
        var sources = await db.Sources.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Key, cancellationToken);
        var affected = await db.EntryAffected.AsNoTracking().Where(a => a.EntryId == entry.Id).ToListAsync(cancellationToken);
        var references = await db.EntryReferences.AsNoTracking().Where(r => r.EntryId == entry.Id).OrderBy(r => r.Url).ToListAsync(cancellationToken);
        var details = entry.Details?.Deserialize<Details>(NormalizeRecordsHandler.Json) ?? new Details([], [], []);
        var evidence = entry.ExploitationEvidence?.Deserialize<List<ExploitationEvidence>>(NormalizeRecordsHandler.Json) ?? [];

        return Results.Ok(new EntryDetail(
            Summary(entry, aliases.GetValueOrDefault(entry.Id) ?? []),
            entry.Summary,
            entry.Description,
            entry.CvssVersion,
            entry.CvssVector,
            entry.CweIds,
            entry.Provenance.Deserialize<Dictionary<string, string>>() ?? [],
            [.. evidence.Select(e => new EvidenceView(e.Source, e.State, e.Evidence, e.Since))],
            [.. details.Metrics.Select(m => new MetricView(m.System, m.Version, m.Vector, m.Score, m.Assigner))],
            details.Solutions,
            details.Workarounds,
            [.. affected.Select(a => new AffectedView(
                a.Vendor, a.Product, a.ProductKey,
                a.CpeVendor is null ? null : $"{a.CpeVendor}:{a.CpeProduct}",
                a.PurlType is null ? null : $"pkg:{a.PurlType}/{a.PurlName}",
                a.VersionScheme, a.Introduced, a.Fixed, a.LastAffected, a.Exact, a.Status, a.DefaultStatus,
                sources.GetValueOrDefault(a.SourceId) ?? "unknown"))],
            [.. references.Select(r => new ReferenceView(r.Url, r.Tags))],
            entry.Revision,
            redirectedFrom));
    }

    private static async Task<IResult> ChangesAsync(string key, IntelDbContext db, CancellationToken cancellationToken)
    {
        var (entry, _) = await ResolveAsync(db, key, cancellationToken);
        if (entry is null)
        {
            return NotFound(key);
        }

        var changes = await db.EntryChanges.AsNoTracking().Where(c => c.EntryId == entry.Id).OrderByDescending(c => c.Revision).ToListAsync(cancellationToken);
        return Results.Ok(changes.Select(c => new EntryChangeView(c.Revision, c.ChangedAt, c.ChangeKinds, c.IsMaterial, c.TriggeredBy, c.Diff?.RootElement.Clone())).ToList());
    }

    private static async Task<IResult> SourcesAsync(string key, IntelDbContext db, CancellationToken cancellationToken)
    {
        var (entry, _) = await ResolveAsync(db, key, cancellationToken);
        if (entry is null)
        {
            return NotFound(key);
        }

        var links = await db.EntrySources.AsNoTracking().Where(s => s.EntryId == entry.Id).ToListAsync(cancellationToken);
        var sourceIds = links.Select(l => l.SourceId).Distinct().ToList();
        var sources = await db.Sources.AsNoTracking().Where(s => sourceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        return Results.Ok(links
            .Where(l => sources.ContainsKey(l.SourceId))
            .Select(l => sources[l.SourceId] is var s ? new EntrySourceView(s.Key, s.Name, s.Operator, s.OperatorKind, l.Relation, l.Url, s.Attribution) : null)
            .DistinctBy(v => (v!.Source, v.Relation, v.Url))
            .ToList());
    }

    internal static async Task<(EntryRow? Entry, string? RedirectedFrom)> ResolveAsync(IntelDbContext db, string key, CancellationToken cancellationToken)
    {
        var value = key.Trim();
        var entry = await db.Entries.AsNoTracking().FirstOrDefaultAsync(e => e.PrimaryKey == value, cancellationToken)
            ?? await db.Entries.AsNoTracking().FirstOrDefaultAsync(
                e => db.EntryAliases.Any(a => a.EntryId == e.Id && a.Value == value), cancellationToken);
        string? redirectedFrom = null;
        for (var hops = 0; entry is { State: "merged", MergedIntoId: { } target } && hops < 5; hops++)
        {
            redirectedFrom ??= entry.PrimaryKey;
            entry = await db.Entries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == target, cancellationToken);
        }

        return (entry, redirectedFrom);
    }

    private static async Task<Dictionary<Guid, List<string>>> AliasesAsync(IntelDbContext db, List<Guid> ids, CancellationToken cancellationToken) =>
        (await db.EntryAliases.AsNoTracking().Where(a => ids.Contains(a.EntryId)).ToListAsync(cancellationToken))
            .GroupBy(a => a.EntryId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Value).Order(StringComparer.Ordinal).ToList());

    private static EntrySummary Summary(EntryRow e, IReadOnlyList<string> aliases) => new(
        e.PrimaryKey, e.Kind, e.Title, e.State, e.CvssScore, EntryProjection.SeverityClass(e.CvssScore), e.Exploitation, e.EpssClass,
        e.FixAvailable, e.AuthorityWarning, e.PublishedAt, e.ChangedAt, aliases);

    private static readonly string[] IdentifierPrefixes = ["CVE-", "GHSA-", "PYSEC-", "GO-", "OSV-", "EUVD-", "RUSTSEC-", "GSD-"];

    private static bool LooksLikeIdentifier(string q) =>
        q.Length <= 64 && !q.Contains(' ', StringComparison.Ordinal) && q.Contains('-', StringComparison.Ordinal)
        && (IdentifierPrefixes.Any(p => q.StartsWith(p, StringComparison.OrdinalIgnoreCase)) || q.Contains(':', StringComparison.Ordinal) || q.Any(char.IsAsciiDigit));

    private static IResult NotFound(string key) => ApiProblems.Problem("not_found", StatusCodes.Status404NotFound, $"No entry {key}.");

    private static string WriteCursor(EntryRow last) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{last.ChangedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{last.Id}"));

    private static bool TryReadCursor(string cursor, out DateTimeOffset changedAt, out Guid id)
    {
        changedAt = default;
        id = default;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length == 2 && long.TryParse(parts[0], CultureInfo.InvariantCulture, out var ticks) && Guid.TryParse(parts[1], out id))
            {
                changedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }

    private sealed record Details(List<Ardea.Intel.Sources.Abstractions.SeverityMetric> Metrics, List<string> Solutions, List<string> Workarounds);
}
