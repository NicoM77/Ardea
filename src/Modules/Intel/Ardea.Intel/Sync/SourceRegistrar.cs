using System.Security.Cryptography;
using Ardea.Intel.Persistence;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ardea.Intel.Sync;

internal sealed class SourceRegistrar(IServiceScopeFactory scopes, SourceRegistry registry, TimeProvider time) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken) => await RegisterAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal async Task RegisterAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var existing = await db.Sources.ToDictionaryAsync(s => s.Key, StringComparer.Ordinal, cancellationToken);
        var now = time.GetUtcNow();
        foreach (var descriptor in registry.Descriptors)
        {
            if (!existing.TryGetValue(descriptor.Key, out var row))
            {
                row = new SourceRow
                {
                    Key = descriptor.Key,
                    Name = descriptor.Name,
                    Kind = Kind(descriptor.Kind),
                    Operator = descriptor.Operator,
                    OperatorKind = OperatorKind(descriptor.OperatorKind),
                    Adapter = registry.AdapterName(descriptor.Key),
                    Attribution = descriptor.Attribution,
                    TermsMode = descriptor.Terms == TermsMode.Full ? "full" : "metadata_only",
                    Enabled = descriptor.EnabledByDefault,
                    Interval = descriptor.DefaultInterval,
                    ParserVersion = descriptor.ParserVersion,
                    Health = descriptor.EnabledByDefault ? SourceHealth.Healthy : SourceHealth.Disabled,
                    NextSyncAt = now + Spread(TimeSpan.FromMinutes(2)),
                };
                db.Sources.Add(row);
                continue;
            }

            row.Name = descriptor.Name;
            row.Kind = Kind(descriptor.Kind);
            row.Operator = descriptor.Operator;
            row.OperatorKind = OperatorKind(descriptor.OperatorKind);
            row.Adapter = registry.AdapterName(descriptor.Key);
            row.Attribution = descriptor.Attribution;
            row.TermsMode = descriptor.Terms == TermsMode.Full ? "full" : "metadata_only";
            row.ParserVersion = descriptor.ParserVersion;
            if (row.Interval < descriptor.MinimumInterval)
            {
                row.Interval = descriptor.MinimumInterval;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await QueueOutdatedRecordsAsync(db, scope.ServiceProvider.GetRequiredService<IJobQueue>(), cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
    }

    private async Task QueueOutdatedRecordsAsync(IntelDbContext db, IJobQueue jobs, CancellationToken cancellationToken)
    {
        var sources = await db.Sources.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Id, StringComparer.Ordinal, cancellationToken);
        foreach (var descriptor in registry.Descriptors)
        {
            if (!registry.TryGetProvider(descriptor.Key, out _) || !sources.TryGetValue(descriptor.Key, out var sourceId))
            {
                continue;
            }

            var current = descriptor.ParserVersion;
            var outdated = await db.SourceRecords
                .Where(r => r.SourceId == sourceId && r.ParseStatus != "pending" && (r.ParserVersion == null || r.ParserVersion < current))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ParseStatus, "pending"), cancellationToken);
            if (outdated > 0)
            {
                await jobs.EnqueueAsync(new NormalizeRecords(descriptor.Key), NormalizeRecords.Options(descriptor.Key), cancellationToken);
            }
        }
    }

    internal static TimeSpan Spread(TimeSpan max) => TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32((int)Math.Max(1, max.TotalMilliseconds)));

    internal static string Kind(SourceKind kind) => kind switch
    {
        SourceKind.Record => "record",
        SourceKind.Advisory => "advisory",
        _ => "signal",
    };

    internal static string OperatorKind(Sources.Abstractions.OperatorKind kind) => kind.ToString().ToLowerInvariant();
}
