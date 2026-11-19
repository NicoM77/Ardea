using System.Text.Json;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Intel.Persistence;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sync;
using Ardea.Platform.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ardea.Intel.Correlation;

/// <summary>
/// Recomputes the entry an identifier belongs to (master plan 3.3, 6.2–6.4). The key is an alias
/// (<c>CVE:CVE-2026-1</c>) or, for a warning without identifier, its source record
/// (<c>SRC:bacs-warnings:123</c>).
/// </summary>
internal sealed record CorrelateEntry(string Key) : IJob
{
    public static string Kind => "intel.correlate-entry";

    public static EnqueueOptions Options(string key) => new() { CoalesceKey = $"intel.correlate:{key}" };
}

/// <summary>Fans a material change out to the subscribers, after a short pause so a wave of changes counts once.</summary>
internal sealed record NotifyEntryChanged(Guid EntryId) : IJob
{
    public static string Kind => "intel.notify-entry-changed";

    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(1);
}

internal sealed partial class CorrelateEntryHandler(IntelDbContext db, IJobQueue jobs, IProductCatalog catalog, TimeProvider time, ILogger<CorrelateEntryHandler> logger)
    : IJobHandler<CorrelateEntry>
{
    /// <summary>Rounds of following shared aliases; identity chains are short in practice.</summary>
    private const int MaxClosureRounds = 5;

    public async Task HandleAsync(CorrelateEntry job, JobContext context, CancellationToken cancellationToken)
    {
        var sources = await db.Sources.AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken);
        if (job.Key.StartsWith("SRC:", StringComparison.Ordinal))
        {
            await CorrelateWarningAsync(job.Key, sources, cancellationToken);
            return;
        }

        // Identity: everything that shares an alias belongs together (master plan 6.2 rule 1).
        var (aliasSet, observations) = await ClosureAsync(job.Key, exclude: [], cancellationToken);
        var keyArray = aliasSet.ToArray();
        var linked = await db.EntryAliases.Where(a => keyArray.Contains(a.Scheme + ":" + a.Value)).Select(a => a.EntryId).Distinct().ToListAsync(cancellationToken);
        var entries = await db.Entries.Where(e => linked.Contains(e.Id) && e.State != "merged").ToListAsync(cancellationToken);

        var cveEntries = entries.Where(e => IsCve(e.PrimaryKey)).OrderBy(e => e.PrimaryKey, StringComparer.Ordinal).ToList();
        if (cveEntries.Count >= 2)
        {
            // Two entries that both carry their own CVE id are never merged automatically (master plan 6.2 rule 5).
            await SeparateAsync(cveEntries, observations, sources, job.Key, cancellationToken);
            return;
        }

        var (projection, triggeredBy) = Project(observations, aliasSet, sources);
        if (projection is null)
        {
            return;
        }

        var target = entries.FirstOrDefault(e => e.PrimaryKey == projection.PrimaryKey) ?? entries.OrderBy(e => e.Id).FirstOrDefault();
        await MergeIntoAsync(target, entries.Where(e => e != target), cancellationToken);
        projection = await WithSignalsAsync(projection, sources, cancellationToken);
        projection = await WithCatalogKeysAsync(projection, cancellationToken);
        await WriteAsync(target, projection, triggeredBy, cancellationToken);
    }

    /// <summary>
    /// Follows shared aliases from <paramref name="key"/>; observations that carry one of
    /// <paramref name="exclude"/> are not followed (they lead to an entry kept apart).
    /// </summary>
    private async Task<(HashSet<string> Aliases, List<ObservationRow> Observations)> ClosureAsync(string key, IReadOnlyCollection<string> exclude, CancellationToken cancellationToken)
    {
        var aliasSet = new HashSet<string>([key], StringComparer.Ordinal);
        List<ObservationRow> observations = [];
        for (var round = 0; round < MaxClosureRounds; round++)
        {
            var keys = aliasSet.ToArray();
            observations = (await db.Observations.AsNoTracking().Where(o => o.Aliases.Any(a => keys.Contains(a))).ToListAsync(cancellationToken))
                .Where(o => !o.Aliases.Any(exclude.Contains))
                .ToList();
            var before = aliasSet.Count;
            aliasSet.UnionWith(observations.SelectMany(o => o.Aliases));
            if (aliasSet.Count == before)
            {
                break;
            }
        }

        return (aliasSet, observations);
    }

    /// <summary>The merged view of the claim blocks that name one of the aliases; <c>null</c> when none does.</summary>
    private static (EntryProjection? Projection, string TriggeredBy) Project(List<ObservationRow> observations, HashSet<string> aliasSet, Dictionary<Guid, SourceRow> sources)
    {
        var contributions = observations
            .Select(o => (Row: o, Observation: JsonSerializer.Deserialize<Observation>(o.Claims, NormalizeRecordsHandler.Json)!))
            .Select(o => new Contribution(
                o.Row.SourceId,
                sources[o.Row.SourceId].Key,
                sources[o.Row.SourceId].OperatorKind,
                o.Row.SourceRecordId,
                null,
                o.Observation,
                [.. o.Observation.Claims.Where(b => b.Aliases.Any(a => aliasSet.Contains(AliasKey(a))))]))
            .Where(c => c.Claims.Count > 0)
            .ToList();
        return contributions.Count == 0 ? (null, string.Empty) : (Projection.Compute(contributions), contributions[0].SourceKey);
    }

    /// <summary>The later discovered shared identifier joins the entries; the losers stay as redirects (6.2 rule 4).</summary>
    private async Task MergeIntoAsync(EntryRow? target, IEnumerable<EntryRow> losers, CancellationToken cancellationToken)
    {
        foreach (var loser in losers)
        {
            loser.State = "merged";
            loser.MergedIntoId = target!.Id;
            loser.ChangedAt = time.GetUtcNow();
            await db.EntryAliases.Where(a => a.EntryId == loser.Id).ExecuteDeleteAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Keeps entries with different CVE ids apart: each is built from what its own CVE id reaches
    /// without passing an observation that names another of them. Identifiers both sides reach (the
    /// bridge, e.g. a GHSA id listing both CVE ids) belong to neither; the case goes to the review
    /// list. Entries without a CVE id that only one side reaches merge into that side as usual.
    /// </summary>
    private async Task SeparateAsync(List<EntryRow> cveEntries, List<ObservationRow> joined, Dictionary<Guid, SourceRow> sources, string via, CancellationToken cancellationToken)
    {
        var keys = cveEntries.ToDictionary(e => e.Id, e => "CVE:" + e.PrimaryKey);
        var closures = new Dictionary<Guid, (HashSet<string> Aliases, List<ObservationRow> Observations)>();
        foreach (var entry in cveEntries)
        {
            closures[entry.Id] = await ClosureAsync(keys[entry.Id], [.. keys.Values.Where(k => k != keys[entry.Id])], cancellationToken);
        }

        // Reached from more than one side, or named by an observation that names several of the CVE ids.
        var shared = closures.Values.SelectMany(c => c.Aliases).GroupBy(a => a, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key)
            .Concat(joined.Where(o => o.Aliases.Count(keys.ContainsValue) > 1).SelectMany(o => o.Aliases))
            .Where(a => !keys.ContainsValue(a))
            .ToHashSet(StringComparer.Ordinal);

        await RecordConflictAsync(cveEntries, shared, via, cancellationToken);

        // Free identifiers that move or become shared before any entry writes its own.
        foreach (var entry in cveEntries)
        {
            var own = closures[entry.Id].Aliases.Where(a => !shared.Contains(a)).ToArray();
            await db.EntryAliases.Where(a => a.EntryId == entry.Id && !own.Contains(a.Scheme + ":" + a.Value)).ExecuteDeleteAsync(cancellationToken);
        }

        foreach (var entry in cveEntries)
        {
            var aliasSet = closures[entry.Id].Aliases.Where(a => !shared.Contains(a)).ToHashSet(StringComparer.Ordinal);
            var (projection, triggeredBy) = Project(closures[entry.Id].Observations, aliasSet, sources);
            if (projection is null)
            {
                continue;
            }

            var own = aliasSet.ToArray();
            var others = await db.EntryAliases.Where(a => own.Contains(a.Scheme + ":" + a.Value) && a.EntryId != entry.Id).Select(a => a.EntryId).Distinct().ToListAsync(cancellationToken);
            var losers = await db.Entries.Where(e => others.Contains(e.Id) && e.State != "merged" && !keys.Keys.Contains(e.Id)).ToListAsync(cancellationToken);
            await MergeIntoAsync(entry, losers.Where(l => !IsCve(l.PrimaryKey)), cancellationToken);

            projection = projection with { Aliases = [.. projection.Aliases.Where(a => !shared.Contains(AliasKey(a)))] };
            projection = await WithSignalsAsync(projection, sources, cancellationToken);
            projection = await WithCatalogKeysAsync(projection, cancellationToken);
            await WriteAsync(entry, projection, triggeredBy, cancellationToken);
        }
    }

    /// <summary>Opens the case on the review list, or notes that it was seen again; a decided case stays decided.</summary>
    private async Task RecordConflictAsync(List<EntryRow> cveEntries, HashSet<string> shared, string via, CancellationToken cancellationToken)
    {
        var subject = string.Join(",", cveEntries.Select(e => e.PrimaryKey));
        var now = time.GetUtcNow();
        var item = await db.ReviewItems.SingleOrDefaultAsync(i => i.Kind == "cve_conflict" && i.SubjectKey == subject, cancellationToken);
        if (item is null)
        {
            LogConflict(subject, via);
            item = db.ReviewItems.Add(new ReviewItemRow { Id = Guid.CreateVersion7(), Kind = "cve_conflict", SubjectKey = subject, State = "open", FirstSeenAt = now }).Entity;
        }

        item.EntryIds = [.. cveEntries.Select(e => e.Id)];
        item.SharedAliases = [.. shared.Order(StringComparer.Ordinal)];
        item.LastSeenAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Folds the signals of the entry's identifiers in (master plan 4.4): a KEV listing means active
    /// exploitation with its evidence; the EPSS class is high when any identifier's class is high.
    /// </summary>
    private async Task<EntryProjection> WithSignalsAsync(EntryProjection projection, Dictionary<Guid, SourceRow> sources, CancellationToken cancellationToken)
    {
        var keys = projection.Aliases.Select(a => a.Scheme.ToUpperInvariant() + ":" + a.Value).ToArray();
        var signals = await db.EntrySignals.AsNoTracking().Where(s => keys.Contains(s.Scheme + ":" + s.Value)).ToListAsync(cancellationToken);
        if (signals.Count == 0)
        {
            return projection;
        }

        var kev = signals.Where(s => s.KevListedOn is not null).OrderBy(s => s.KevListedOn).FirstOrDefault();
        var evidence = projection.ExploitationEvidence.ToList();
        var provenance = new Dictionary<string, string>(projection.Provenance, StringComparer.Ordinal);
        var links = projection.Sources.ToList();
        if (kev is not null)
        {
            evidence.Add(new ExploitationEvidence("cisa-kev", "active", kev.KevUrl, kev.KevListedOn?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
            provenance["exploitation"] = "cisa-kev";
            AddSignalLink(links, sources, "cisa-kev", kev.KevUrl);
        }

        var epss = signals.Any(s => s.EpssClass == "high") ? "high" : signals.Any(s => s.EpssClass == "low") ? "low" : null;
        if (epss is not null)
        {
            provenance["epss"] = "first-epss";
            AddSignalLink(links, sources, "first-epss", null);
        }

        return projection with
        {
            Exploitation = kev is not null ? "active" : projection.Exploitation,
            ExploitationEvidence = evidence,
            EpssClass = epss,
            Provenance = provenance,
            Sources = links,
        };
    }

    /// <summary>
    /// The catalogue's key for every affected product it knows by CPE pair, package or name (master
    /// plan 6.5); unknown products keep their raw key and stay findable.
    /// </summary>
    private async Task<EntryProjection> WithCatalogKeysAsync(EntryProjection projection, CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<(string?, string?, string?, string?, string?, string?), string?>();
        var affected = new List<AffectedData>(projection.Affected.Count);
        foreach (var row in projection.Affected)
        {
            var lookup = (row.Vendor, row.Product, row.CpeVendor, row.CpeProduct, row.PurlType, row.PurlName);
            if (!resolved.TryGetValue(lookup, out var key))
            {
                var name = row.Product ?? row.PurlName;
                CpePair? cpe = row.CpeVendor is not null && row.CpeProduct is not null ? new CpePair(row.CpeVendor, row.CpeProduct) : null;
                PackageCoordinate? package = row.PurlType is not null && row.PurlName is not null ? new PackageCoordinate(row.PurlType, row.PurlName) : null;
                key = name is null && cpe is null ? row.ProductKey
                    : (await catalog.ResolveKeyAsync(row.Vendor, name ?? cpe!.Value.Product, cpe, package, cancellationToken))?.Value ?? row.ProductKey;
                resolved[lookup] = key;
            }

            affected.Add(row with { ProductKey = key });
        }

        return projection with { Affected = affected };
    }

    private static void AddSignalLink(List<SourceLink> links, Dictionary<Guid, SourceRow> sources, string key, string? url)
    {
        if (sources.Values.FirstOrDefault(s => s.Key == key) is { } source)
        {
            links.Add(new SourceLink(source.Id, null, "signal", url));
        }
    }

    private async Task CorrelateWarningAsync(string key, Dictionary<Guid, SourceRow> sources, CancellationToken cancellationToken)
    {
        var parts = key.Split(':', 3);
        var source = sources.Values.FirstOrDefault(s => s.Key == parts[1]);
        if (source is null || parts.Length < 3)
        {
            return;
        }

        var record = await db.SourceRecords.AsNoTracking().SingleOrDefaultAsync(r => r.SourceId == source.Id && r.ExternalId == parts[2], cancellationToken);
        var observation = record is null ? null : await db.Observations.AsNoTracking().SingleOrDefaultAsync(o => o.SourceRecordId == record.Id, cancellationToken);
        if (observation is null)
        {
            return;
        }

        var model = JsonSerializer.Deserialize<Observation>(observation.Claims, NormalizeRecordsHandler.Json)!;
        var projection = Projection.Compute([new Contribution(source.Id, source.Key, source.OperatorKind, record!.Id, record.Url, model, [])], $"{source.Key}:{record.ExternalId}");
        var entry = await db.Entries.SingleOrDefaultAsync(e => e.PrimaryKey == projection.PrimaryKey, cancellationToken);
        await WriteAsync(entry, projection, source.Key, cancellationToken);
    }

    /// <summary>Writes the projection; a revision and a history line only when something changed (master plan 6.4).</summary>
    private async Task WriteAsync(EntryRow? entry, EntryProjection projection, string triggeredBy, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var created = entry is null;
        entry ??= db.Entries.Add(new EntryRow
        {
            Id = Guid.CreateVersion7(),
            Kind = projection.Kind,
            PrimaryKey = projection.PrimaryKey,
            State = projection.State,
            Exploitation = projection.Exploitation,
            MaterialFingerprint = string.Empty,
            RemediationFingerprint = string.Empty,
            Provenance = JsonDocument.Parse("{}"),
        }).Entity;

        var before = created ? null : await SnapshotAsync(entry, cancellationToken);
        var material = projection.MaterialFingerprint(projection.EpssClass == "high" ? "high" : null);
        var epssChanged = (entry.EpssClass == "high") != (projection.EpssClass == "high");
        entry.EpssClass = projection.EpssClass;
        var remediation = projection.RemediationFingerprint();

        entry.Kind = projection.Kind;
        entry.Title = projection.Title;
        entry.Summary = projection.Summary;
        entry.Description = projection.Description;
        entry.State = projection.State;
        entry.PublishedAt = projection.PublishedAt;
        entry.CvssScore = projection.CvssScore;
        entry.CvssVersion = projection.CvssVersion;
        entry.CvssVector = projection.CvssVector;
        entry.Exploitation = projection.Exploitation;
        entry.ExploitationEvidence = projection.ExploitationEvidence.Count == 0 ? null : JsonSerializer.SerializeToDocument(projection.ExploitationEvidence, NormalizeRecordsHandler.Json);
        entry.FixAvailable = projection.FixAvailable;
        entry.CweIds = [.. projection.CweIds];
        entry.FixVersionsInText = [.. projection.FixVersionsInText];
        entry.AuthorityWarning = projection.AuthorityWarning;
        entry.Provenance = JsonSerializer.SerializeToDocument(projection.Provenance);
        entry.Details = JsonSerializer.SerializeToDocument(new { metrics = projection.AllMetrics, solutions = projection.Solutions, workarounds = projection.Workarounds }, NormalizeRecordsHandler.Json);

        var after = Snapshot(projection);
        var kinds = created ? ["created"] : Compare(before!, after, entry.MaterialFingerprint != material, entry.RemediationFingerprint != remediation);
        if (!created && epssChanged)
        {
            kinds.Remove("material");
            kinds.Add("epss");
        }
        if (kinds.Count == 0)
        {
            // Same observations, same entry: nothing to write beyond what is already there.
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        await ReplaceChildrenAsync(entry.Id, projection, created, cancellationToken);
        var isMaterial = created || entry.MaterialFingerprint != material;
        entry.MaterialFingerprint = material;
        entry.RemediationFingerprint = remediation;
        entry.Revision++;
        entry.ChangedAt = now;
        db.EntryChanges.Add(new EntryChangeRow
        {
            Id = Guid.CreateVersion7(),
            EntryId = entry.Id,
            Revision = entry.Revision,
            ChangedAt = now,
            ChangeKinds = [.. kinds],
            IsMaterial = isMaterial,
            Diff = created ? null : JsonSerializer.SerializeToDocument(Diff(before!, after), NormalizeRecordsHandler.Json),
            TriggeredBy = triggeredBy,
        });
        await db.SaveChangesAsync(cancellationToken);

        if (isMaterial)
        {
            await jobs.EnqueueAsync(new NotifyEntryChanged(entry.Id), new EnqueueOptions { CoalesceKey = $"intel.entry-changed:{entry.Id}", Delay = NotifyEntryChanged.Delay }, cancellationToken);
        }
    }

    private async Task ReplaceChildrenAsync(Guid entryId, EntryProjection projection, bool created, CancellationToken cancellationToken)
    {
        if (!created)
        {
            await db.EntryAliases.Where(a => a.EntryId == entryId).ExecuteDeleteAsync(cancellationToken);
            await db.EntryAffected.Where(a => a.EntryId == entryId).ExecuteDeleteAsync(cancellationToken);
            await db.EntryReferences.Where(r => r.EntryId == entryId).ExecuteDeleteAsync(cancellationToken);
            await db.EntrySources.Where(s => s.EntryId == entryId).ExecuteDeleteAsync(cancellationToken);
        }

        db.EntryAliases.AddRange(projection.Aliases.Select(a => new EntryAliasRow { Id = Guid.CreateVersion7(), EntryId = entryId, Scheme = a.Scheme.ToUpperInvariant(), Value = a.Value }));
        db.EntryAffected.AddRange(projection.Affected.Select(a => new EntryAffectedRow
        {
            Id = Guid.CreateVersion7(),
            EntryId = entryId,
            SourceId = a.SourceId,
            Vendor = Cut(a.Vendor),
            Product = Cut(a.Product),
            ProductKey = Cut(a.ProductKey),
            CpeVendor = Cut(a.CpeVendor),
            CpeProduct = Cut(a.CpeProduct),
            PurlType = a.PurlType,
            PurlName = Cut(a.PurlName),
            VersionScheme = a.VersionScheme,
            Introduced = a.Introduced,
            Fixed = a.Fixed,
            LastAffected = a.LastAffected,
            Exact = a.Exact,
            Status = a.Status,
            DefaultStatus = a.DefaultStatus,
        }));
        db.EntryReferences.AddRange(projection.References.Select(r => new EntryReferenceRow { Id = Guid.CreateVersion7(), EntryId = entryId, Url = r.Url.Length <= 2000 ? r.Url : r.Url[..2000], Tags = r.Tags, SourceId = r.SourceId }));
        db.EntrySources.AddRange(projection.Sources.Select(s => new EntrySourceRow { Id = Guid.CreateVersion7(), EntryId = entryId, SourceId = s.SourceId, SourceRecordId = s.SourceRecordId, Relation = s.Relation, Url = s.Url }));
    }

    private static string? Cut(string? value) => value is null || value.Length <= 500 ? value : value[..500];

    private sealed record EntrySnapshot(
        string? Title, string? Summary, string? Description, string State, double? Score, string? Vector, string Exploitation,
        bool Fix, bool Authority, string Aliases, string Affected, string References, string Cwes);

    private async Task<EntrySnapshot> SnapshotAsync(EntryRow entry, CancellationToken cancellationToken)
    {
        var aliases = await db.EntryAliases.Where(a => a.EntryId == entry.Id).Select(a => a.Scheme + ":" + a.Value).ToListAsync(cancellationToken);
        var affected = await db.EntryAffected.Where(a => a.EntryId == entry.Id).ToListAsync(cancellationToken);
        var references = await db.EntryReferences.Where(r => r.EntryId == entry.Id).Select(r => r.Url).ToListAsync(cancellationToken);
        return new EntrySnapshot(
            entry.Title, entry.Summary, entry.Description, entry.State, entry.CvssScore, entry.CvssVector, entry.Exploitation,
            entry.FixAvailable, entry.AuthorityWarning,
            Join(aliases),
            Join(affected.Select(a => string.Join('|', a.SourceId, a.ProductKey, a.CpeVendor, a.CpeProduct, a.PurlType, a.PurlName, a.VersionScheme, a.Introduced, a.Fixed, a.LastAffected, a.Exact, a.Status, a.DefaultStatus))),
            Join(references),
            Join(entry.CweIds));
    }

    private static EntrySnapshot Snapshot(EntryProjection p) => new(
        p.Title, p.Summary, p.Description, p.State, p.CvssScore, p.CvssVector, p.Exploitation, p.FixAvailable, p.AuthorityWarning,
        Join(p.Aliases.Select(a => a.Scheme.ToUpperInvariant() + ":" + a.Value)),
        Join(p.Affected.Select(a => string.Join('|', a.SourceId, a.ProductKey, a.CpeVendor, a.CpeProduct, a.PurlType, a.PurlName, a.VersionScheme, a.Introduced, a.Fixed, a.LastAffected, a.Exact, a.Status, a.DefaultStatus))),
        Join(p.References.Select(r => r.Url)),
        Join(p.CweIds));

    private static string Join(IEnumerable<string> values) => string.Join('\n', values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private static List<string> Compare(EntrySnapshot before, EntrySnapshot after, bool materialChanged, bool remediationChanged)
    {
        var kinds = new List<string>();
        void Check(bool changed, string kind)
        {
            if (changed)
            {
                kinds.Add(kind);
            }
        }

        Check(before.State != after.State, "state");
        Check(before.Score != after.Score || before.Vector != after.Vector, "severity");
        Check(before.Affected != after.Affected, "affected");
        Check(before.Exploitation != after.Exploitation, "exploitation");
        Check(before.Fix != after.Fix, "fix");
        Check(before.Authority != after.Authority, "authority");
        Check(before.Aliases != after.Aliases, "aliases");
        Check(before.Title != after.Title || before.Summary != after.Summary || before.Description != after.Description, "text");
        Check(before.References != after.References, "references");
        Check(before.Cwes != after.Cwes, "weaknesses");
        Check(remediationChanged, "remediation");
        if (materialChanged && !kinds.Any(k => k is "state" or "severity" or "affected" or "exploitation" or "fix" or "authority"))
        {
            kinds.Add("material");
        }

        return kinds;
    }

    private static Dictionary<string, object?> Diff(EntrySnapshot before, EntrySnapshot after)
    {
        var diff = new Dictionary<string, object?>(StringComparer.Ordinal);
        void Add<T>(string name, T old, T @new)
        {
            if (!EqualityComparer<T>.Default.Equals(old, @new))
            {
                diff[name] = new { old, @new };
            }
        }

        Add("title", before.Title, after.Title);
        Add("state", before.State, after.State);
        Add("cvssScore", before.Score, after.Score);
        Add("cvssVector", before.Vector, after.Vector);
        Add("exploitation", before.Exploitation, after.Exploitation);
        Add("fixAvailable", before.Fix, after.Fix);
        Add("authorityWarning", before.Authority, after.Authority);
        return diff;
    }

    internal static string AliasKey(Identifier identifier) => $"{identifier.Scheme.ToUpperInvariant()}:{identifier.Value}";

    private static bool IsCve(string primaryKey) => primaryKey.StartsWith("CVE-", StringComparison.Ordinal);

    [LoggerMessage(EventId = 4201, Level = LogLevel.Warning, Message = "Entries {Entries} share aliases but carry different CVE ids; kept apart and put on the review list (via {Key})")]
    private partial void LogConflict(string entries, string key);
}

internal sealed class NotifyEntryChangedHandler(IntelDbContext db, IEnumerable<IEntryChangeSubscriber> subscribers) : IJobHandler<NotifyEntryChanged>
{
    public async Task HandleAsync(NotifyEntryChanged job, JobContext context, CancellationToken cancellationToken)
    {
        var entry = await db.Entries.AsNoTracking().SingleOrDefaultAsync(e => e.Id == job.EntryId, cancellationToken);
        if (entry is null)
        {
            return;
        }

        var kinds = await db.EntryChanges.AsNoTracking().Where(c => c.EntryId == entry.Id && c.IsMaterial)
            .OrderByDescending(c => c.Revision).Select(c => c.ChangeKinds).FirstOrDefaultAsync(cancellationToken) ?? [];
        var change = new EntryChanged(entry.Id, entry.Revision, kinds);
        foreach (var subscriber in subscribers)
        {
            await subscriber.OnEntryChangedAsync(change, cancellationToken);
        }
    }
}
