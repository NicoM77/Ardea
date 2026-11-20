using Ardea.Intel.Http;
using Ardea.Intel.Persistence;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ardea.Intel.Sync;

internal sealed record CheckCompleteness : IJob
{
    public static string Kind => "intel.check-completeness";

    public static readonly TimeSpan Window = TimeSpan.FromDays(7);
}

internal sealed partial class CheckCompletenessHandler(
    IUnitOfWorkFactory units, IJobQueue jobs, SourceRegistry registry, SourceHttpFactory http, TimeProvider time, ILogger<CheckCompletenessHandler> logger)
    : IJobHandler<CheckCompleteness>
{
    internal const int BatchSize = 100;

    public async Task HandleAsync(CheckCompleteness job, JobContext context, CancellationToken cancellationToken)
    {
        List<(Guid Id, string Key)> sources;
        await using (var scope = units.CreateScope())
        {
            sources = (await scope.ServiceProvider.GetRequiredService<IntelDbContext>().Sources.AsNoTracking().Where(s => s.Enabled).ToListAsync(cancellationToken))
                .Select(s => (s.Id, s.Key)).ToList();
        }

        foreach (var (sourceId, key) in sources)
        {
            if (registry.TryGetProvider(key, out var provider) && provider is IChangeLogSource log)
            {
                var missing = await FindMissingAsync(sourceId, key, provider.Descriptor, log, cancellationToken);
                foreach (var batch in missing.Chunk(BatchSize))
                {
                    await jobs.EnqueueAsync(new SyncSource(key, batch), cancellationToken: cancellationToken);
                }

                LogChecked(key, missing.Count);
            }
        }
    }

    internal async Task<List<string>> FindMissingAsync(Guid sourceId, string key, SourceDescriptor descriptor, IChangeLogSource log, CancellationToken cancellationToken)
    {
        var expected = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
        using (var client = http.Create(descriptor, SyncMode.Incremental))
        {
            var context = new SyncContext
            {
                SourceKey = key,
                Http = client,
                Mode = SyncMode.Incremental,
                Clock = time,
                Limits = new SyncLimits(descriptor.MaxResponseBytes, TimeSpan.FromMinutes(10), int.MaxValue),
                WorkDirectory = Path.GetTempPath(),
            };
            await foreach (var change in log.ListChangesAsync(context, time.GetUtcNow() - CheckCompleteness.Window, cancellationToken))
            {
                if (!expected.TryGetValue(change.ExternalId, out var known) || (change.ModifiedAt > known))
                {
                    expected[change.ExternalId] = change.ModifiedAt;
                }
            }
        }

        var missing = new List<string>();
        await using var scope = units.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        foreach (var chunk in expected.Keys.Chunk(1000))
        {
            var stored = await db.SourceRecords.AsNoTracking()
                .Where(r => r.SourceId == sourceId && chunk.Contains(r.ExternalId))
                .Select(r => new { r.ExternalId, r.SourceModifiedAt })
                .ToDictionaryAsync(r => r.ExternalId, r => r.SourceModifiedAt, StringComparer.Ordinal, cancellationToken);
            foreach (var id in chunk)
            {
                if (!stored.TryGetValue(id, out var modified)
                    || (expected[id] is { } logged && (modified is null || modified < logged - TimeSpan.FromSeconds(1))))
                {
                    missing.Add(id);
                }
            }
        }

        return missing;
    }

    [LoggerMessage(EventId = 4301, Level = LogLevel.Information, Message = "Completeness of {Source}: {Missing} record(s) to fetch")]
    private partial void LogChecked(string source, int missing);
}
