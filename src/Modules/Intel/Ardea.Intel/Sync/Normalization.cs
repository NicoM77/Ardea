using System.Text.Json;
using System.Text.Json.Serialization;
using Ardea.Intel.Persistence;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ardea.Intel.Sync;

internal sealed record NormalizeRecords(string SourceKey) : IJob
{
    public static string Kind => "intel.normalize-records";

    public static EnqueueOptions Options(string sourceKey) => new() { CoalesceKey = $"intel.normalize:{sourceKey}" };
}

internal sealed partial class NormalizeRecordsHandler(IUnitOfWorkFactory units, SourceRegistry registry, TimeProvider time, ILogger<NormalizeRecordsHandler> logger)
    : IJobHandler<NormalizeRecords>
{
    internal const int BatchSize = 200;

    internal const double QuarantineThreshold = 0.05;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task HandleAsync(NormalizeRecords job, JobContext context, CancellationToken cancellationToken)
    {
        if (!registry.TryGetProvider(job.SourceKey, out var provider))
        {
            return;
        }

        int normalized = 0, quarantined = 0;
        while (true)
        {
            var (done, bad) = await NormalizeBatchAsync(job.SourceKey, provider, cancellationToken);
            normalized += done;
            quarantined += bad;
            if (done < BatchSize)
            {
                break;
            }
        }

        if (normalized >= 20 && (double)quarantined / normalized > QuarantineThreshold)
        {
            await MarkDegradedAsync(job.SourceKey, quarantined, normalized, cancellationToken);
        }

        LogNormalized(job.SourceKey, normalized, quarantined);
    }

    private async Task<(int Normalized, int Quarantined)> NormalizeBatchAsync(string sourceKey, ISourceProvider provider, CancellationToken cancellationToken)
    {
        await using var scope = units.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var source = await db.Sources.SingleAsync(s => s.Key == sourceKey, cancellationToken);
        var records = await db.SourceRecords
            .Where(r => r.SourceId == source.Id && r.ParseStatus == "pending")
            .OrderBy(r => r.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (records.Count == 0)
        {
            return (0, 0);
        }

        var ids = records.Select(r => r.Id).ToList();
        var existing = await db.Observations.Where(o => ids.Contains(o.SourceRecordId)).ToDictionaryAsync(o => o.SourceRecordId, cancellationToken);
        var now = time.GetUtcNow();
        var quarantined = 0;
        var correlate = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var raw = new RawRecord(record.ExternalId, record.Payload, record.ContentType)
            {
                Url = record.Url is null ? null : new Uri(record.Url),
                PublishedAt = record.SourcePublishedAt,
                ModifiedAt = record.SourceModifiedAt,
            };

            NormalizationResult result;
            try
            {
                result = provider.Normalize(raw);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = NormalizationResult.Quarantine($"Parser error: {ex.GetType().Name}: {ex.Message}");
            }

            record.ParserVersion = provider.Descriptor.ParserVersion;
            if (result.Observation is not { } observation)
            {
                record.ParseStatus = "quarantined";
                record.QuarantineReason = Truncate(result.QuarantineReason ?? "No reason given.");
                quarantined++;
                if (existing.TryGetValue(record.Id, out var stale))
                {
                    correlate.UnionWith(stale.Aliases);
                    db.Observations.Remove(stale);
                }

                continue;
            }

            record.ParseStatus = "parsed";
            record.QuarantineReason = null;
            var row = existing.GetValueOrDefault(record.Id) ?? db.Observations.Add(new ObservationRow
            {
                Id = Guid.CreateVersion7(),
                SourceRecordId = record.Id,
                SourceId = source.Id,
                Kind = "vulnerability",
                Claims = JsonDocument.Parse("[]"),
                Origin = "parsed",
            }).Entity;
            row.RecordRevision = record.Revision;
            row.Kind = observation.Kind switch
            {
                ObservationKind.Advisory => "advisory",
                ObservationKind.Warning => "warning",
                _ => "vulnerability",
            };
            row.Title = Truncate(observation.Title, 1000);
            row.Description = observation.Description;
            row.Language = observation.Language;
            var previous = existing.TryGetValue(record.Id, out var old) ? old.Aliases : [];
            var claims = JsonSerializer.SerializeToDocument(observation, Json);
            var changed = old is null || !JsonElement.DeepEquals(old.Claims.RootElement, claims.RootElement);
            row.Claims = claims;
            row.Aliases = [.. observation.Claims.SelectMany(c => c.Aliases).Select(Correlation.CorrelateEntryHandler.AliasKey).Distinct(StringComparer.Ordinal)];
            if (changed)
            {
                correlate.UnionWith(row.Aliases.Length > 0 ? row.Aliases.Concat(previous) : [$"SRC:{sourceKey}:{record.ExternalId}"]);
            }
            row.ParserVersion = provider.Descriptor.ParserVersion;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        var jobs = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        foreach (var key in correlate)
        {
            await jobs.EnqueueAsync(new Correlation.CorrelateEntry(key), Correlation.CorrelateEntry.Options(key), cancellationToken);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        return (records.Count, quarantined);
    }

    private async Task MarkDegradedAsync(string sourceKey, int quarantined, int normalized, CancellationToken cancellationToken)
    {
        await using var scope = units.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var reason = $"{quarantined} of {normalized} records could not be read (quarantine above {QuarantineThreshold:P0}).";
        await db.Sources.Where(s => s.Key == sourceKey && s.Health == SourceHealth.Healthy)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Health, SourceHealth.Degraded).SetProperty(x => x.HealthReason, reason), cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
    }

    private static string Truncate(string value) => value.Length <= 2000 ? value : value[..2000];

    private static string? Truncate(string? value, int length) => value is null || value.Length <= length ? value : value[..length];

    [LoggerMessage(EventId = 4101, Level = LogLevel.Information, Message = "Source {Source}: normalised {Normalized}, quarantined {Quarantined}")]
    private partial void LogNormalized(string source, int normalized, int quarantined);
}
