using Ardea.Intel.Http;
using Ardea.Intel.Persistence;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ardea.Intel.Sync;

/// <summary>Every minute: enqueue a sync for every due source (master plan 5.4).</summary>
internal sealed record ScheduleDueSources : IJob
{
    public static string Kind => "intel.schedule-due-sources";
}

/// <summary>One run of one source. A single attempt: the source's own back-off decides the next run (master plan 3.4).</summary>
internal sealed record SyncSource(string SourceKey) : IJob
{
    public static string Kind => "intel.sync-source";

    /// <summary>Upper bound of a run; also how far the due time is pushed while it runs.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(60);
}

internal sealed class ScheduleDueSourcesHandler(IntelDbContext db, IJobQueue jobs, TimeProvider time) : IJobHandler<ScheduleDueSources>
{
    public async Task HandleAsync(ScheduleDueSources job, JobContext context, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await db.Sources
            .FromSqlInterpolated($"SELECT * FROM intel.source WHERE enabled AND next_sync_at <= {now} FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
        foreach (var source in due)
        {
            // No second run while this one works: the due time moves past the run's time limit.
            source.NextSyncAt = now + SyncSource.Timeout + TimeSpan.FromMinutes(5);
            await jobs.EnqueueAsync(new SyncSource(source.Key), new EnqueueOptions { CoalesceKey = $"intel.sync:{source.Key}" }, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Runs a provider (master plan 5.2, 5.4, 9.2): read the state in a short unit of work, fetch without
/// an open transaction, store records and cursor at every checkpoint in a unit of work of their own,
/// then record the outcome and the next due time in the job's unit of work.
/// </summary>
internal sealed partial class SyncSourceHandler(
    IUnitOfWorkFactory units,
    IntelDbContext db,
    SourceRegistry registry,
    SourceHttpFactory http,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<SyncSourceHandler> logger) : IJobHandler<SyncSource>
{
    /// <summary>Records stored in one transaction when a provider delivers many between checkpoints.</summary>
    internal const int FlushSize = 500;

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(6);

    public async Task HandleAsync(SyncSource job, JobContext context, CancellationToken cancellationToken)
    {
        if (!registry.TryGetProvider(job.SourceKey, out var provider))
        {
            // Signal sources run through their own path (task 1.8); an unknown key is a removed adapter.
            LogNoProvider(job.SourceKey);
            return;
        }

        var (sourceId, cursor, interval, settings) = await ReadStateAsync(job.SourceKey, cancellationToken);
        var startedAt = time.GetUtcNow();
        var mode = cursor is null ? SyncMode.Backfill : SyncMode.Incremental;
        var runId = await StartRunAsync(sourceId, mode, startedAt, cancellationToken);
        var work = Path.Combine(configuration["SCRATCH_DIR"] ?? Path.Combine(Path.GetTempPath(), "ardea"), job.SourceKey, runId.ToString("N"));
        Directory.CreateDirectory(work);

        var run = new RunCounters();
        SyncContext? sync = null;
        try
        {
            using var client = http.Create(provider.Descriptor, mode);
            sync = new SyncContext
            {
                SourceKey = job.SourceKey,
                Cursor = cursor,
                Http = client,
                Mode = mode,
                BackfillFrom = mode == SyncMode.Backfill ? startedAt - BackfillWindow() : null,
                Clock = time,
                Limits = new SyncLimits(mode == SyncMode.Backfill ? provider.Descriptor.MaxBackfillBytes : provider.Descriptor.MaxResponseBytes, SyncSource.Timeout, int.MaxValue),
                WorkDirectory = work,
                Configuration = settings,
            };

            var buffer = new List<RawRecord>();
            await foreach (var item in provider.FetchAsync(sync, cancellationToken))
            {
                switch (item)
                {
                    case RawRecord record:
                        buffer.Add(record);
                        if (buffer.Count >= FlushSize)
                        {
                            // Records without moving the cursor: safe, a repeat delivery changes nothing.
                            await CommitAsync(sourceId, runId, buffer, newCursor: null, run, cancellationToken);
                        }

                        break;
                    case Checkpoint checkpoint:
                        await CommitAsync(sourceId, runId, buffer, checkpoint.Cursor, run, cancellationToken);
                        break;
                }
            }

            await CommitAsync(sourceId, runId, buffer, newCursor: null, run, cancellationToken);
            await FinishAsync(sourceId, runId, run, interval, sync.NextSyncHint, error: null, cancellationToken);
            LogSucceeded(job.SourceKey, run.Fetched, run.Changed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(ex, job.SourceKey);
            await FinishAsync(sourceId, runId, run, interval, sync?.NextSyncHint, $"{ex.GetType().Name}: {ex.Message}", cancellationToken);
        }
        finally
        {
            TryDelete(work);
        }
    }

    private async Task<(Guid Id, string? Cursor, TimeSpan Interval, IReadOnlyDictionary<string, string> Settings)> ReadStateAsync(string key, CancellationToken cancellationToken)
    {
        await using var scope = units.CreateScope();
        var source = await scope.ServiceProvider.GetRequiredService<IntelDbContext>().Sources.AsNoTracking().SingleAsync(s => s.Key == key, cancellationToken);
        var settings = source.Config.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
            ? source.Config.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal)
            : [];
        return (source.Id, source.Cursor, source.Interval, settings);
    }

    private async Task<Guid> StartRunAsync(Guid sourceId, SyncMode mode, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        await using var scope = units.CreateScope();
        var runDb = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var run = new SourceRunRow
        {
            Id = Guid.CreateVersion7(),
            SourceId = sourceId,
            Mode = mode.ToString().ToLowerInvariant(),
            StartedAt = startedAt,
            Outcome = "running",
        };
        runDb.SourceRuns.Add(run);
        await runDb.Sources.Where(s => s.Id == sourceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastAttemptAt, startedAt), cancellationToken);
        await runDb.SaveChangesAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        return run.Id;
    }

    /// <summary>Records up to a checkpoint and the checkpoint's cursor in one transaction (master plan 5.2).</summary>
    private async Task CommitAsync(Guid sourceId, Guid runId, List<RawRecord> buffer, string? newCursor, RunCounters run, CancellationToken cancellationToken)
    {
        if (buffer.Count == 0 && newCursor is null)
        {
            return;
        }

        await using var scope = units.CreateScope();
        var batchDb = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var stored = buffer.Count > 0
            ? await RawRecordStore.StoreAsync(batchDb, sourceId, buffer, time.GetUtcNow(), cancellationToken)
            : new StoreResult(0, 0, []);
        if (newCursor is not null)
        {
            await batchDb.Sources.Where(s => s.Id == sourceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Cursor, newCursor), cancellationToken);
            run.Checkpoints++;
        }

        run.Fetched += stored.Fetched;
        run.Changed += stored.Changed;
        await batchDb.SourceRuns.Where(r => r.Id == runId).ExecuteUpdateAsync(
            s => s.SetProperty(x => x.Fetched, run.Fetched).SetProperty(x => x.Changed, run.Changed).SetProperty(x => x.Checkpoints, run.Checkpoints),
            cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        buffer.Clear();
    }

    /// <summary>Outcome and next due time, in the job's own unit of work.</summary>
    private async Task FinishAsync(Guid sourceId, Guid runId, RunCounters run, TimeSpan interval, TimeSpan? hint, string? error, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var source = await db.Sources.SingleAsync(s => s.Id == sourceId, cancellationToken);
        var runRow = await db.SourceRuns.SingleAsync(r => r.Id == runId, cancellationToken);
        runRow.FinishedAt = now;
        runRow.Fetched = run.Fetched;
        runRow.Changed = run.Changed;
        runRow.Checkpoints = run.Checkpoints;
        runRow.Outcome = error is null ? "succeeded" : "failed";
        runRow.Error = error is null ? null : error.Length <= 2000 ? error : error[..2000];

        if (error is null)
        {
            source.LastSuccessAt = now;
            source.ConsecutiveFailures = 0;
            source.Health = SourceHealth.Healthy;
            source.HealthReason = null;
            source.NextSyncAt = now + NextInterval(interval, hint);
        }
        else
        {
            source.ConsecutiveFailures++;
            source.Health = source.ConsecutiveFailures >= 3 ? SourceHealth.Failing : SourceHealth.Degraded;
            source.HealthReason = runRow.Error;
            source.NextSyncAt = now + Backoff(interval, source.ConsecutiveFailures, hint);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The interval, or longer if the source asked for it, plus a spread of up to a tenth (at most ten minutes).</summary>
    internal static TimeSpan NextInterval(TimeSpan interval, TimeSpan? hint)
    {
        var baseInterval = hint is { } h && h > interval ? h : interval;
        return baseInterval + SourceRegistrar.Spread(TimeSpan.FromTicks(Math.Min(baseInterval.Ticks / 10, TimeSpan.FromMinutes(10).Ticks)));
    }

    /// <summary>After failures the interval doubles per failure up to six hours (master plan 5.4).</summary>
    internal static TimeSpan Backoff(TimeSpan interval, int failures, TimeSpan? hint)
    {
        var doubled = TimeSpan.FromTicks((long)Math.Min(interval.Ticks * Math.Pow(2, Math.Min(failures, 20)), MaxBackoff.Ticks));
        var delay = hint is { } h && h > doubled ? h : doubled;
        return delay + SourceRegistrar.Spread(TimeSpan.FromTicks(Math.Min(delay.Ticks / 10, TimeSpan.FromMinutes(10).Ticks)));
    }

    private TimeSpan BackfillWindow() =>
        TimeSpan.FromDays(int.TryParse(configuration["INTEL:BACKFILL_DAYS"], out var days) && days > 0 ? days : 180);

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class RunCounters
    {
        public int Fetched { get; set; }

        public int Changed { get; set; }

        public int Checkpoints { get; set; }
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "Source {Source}: fetched {Fetched}, changed {Changed}")]
    private partial void LogSucceeded(string source, int fetched, int changed);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning, Message = "Source {Source}: run failed")]
    private partial void LogFailed(Exception exception, string source);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning, Message = "Source {Source} has no record adapter in this image")]
    private partial void LogNoProvider(string source);
}
