using System.Text.Json;
using Ardea.Intel.Persistence;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sync;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Intel.Demo;

/// <summary>
/// The security records of the demo (<c>ardea seed-demo</c>, task 7.6): a small, curated set of real CVE records of the
/// spike week (11 to 17 November 2026), stored as the CVE Program source would store them, and queued for normalisation.
/// Nothing is fetched. The adapters get their source rows as the worker would give them, but switched off, so a worker
/// that starts next does not load the world into a demo; an operator turns the sources on in the administration.
/// </summary>
internal sealed class DemoSeed(IServiceScopeFactory scopes, SourceRegistry registry, TimeProvider time) : IDemoSeedStep
{
    /// <summary>The key of the CVE Program adapter; the records are in its format.</summary>
    internal const string SourceKey = "cve-program";

    private const string ResourceName = "Ardea.Intel.Demo.records.jsonl";

    public int Order => DemoSeedOrder.Intelligence;

    public string Description => "curated CVE records";

    public async Task SeedAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        await new SourceRegistrar(scopes, registry, time).RegisterAsync(cancellationToken);
        var records = Records();
        await using var scope = context.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<IntelDbContext>();
        await db.Sources.ExecuteUpdateAsync(s => s.SetProperty(x => x.Enabled, false).SetProperty(x => x.Health, SourceHealth.Disabled), cancellationToken);
        var source = await db.Sources.SingleAsync(s => s.Key == SourceKey, cancellationToken);
        await RawRecordStore.StoreAsync(db, source.Id, records, time.GetUtcNow(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await services.GetRequiredService<IJobQueue>().EnqueueAsync(new NormalizeRecords(SourceKey), NormalizeRecords.Options(SourceKey), cancellationToken);
        await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        context.Report($"{records.Count} CVE records stored");
    }

    internal static IReadOnlyList<RawRecord> Records()
    {
        using var stream = typeof(DemoSeed).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The resource {ResourceName} is missing from {typeof(DemoSeed).Assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);
        var records = new List<RawRecord>();
        while (reader.ReadLine() is { Length: > 0 } line)
        {
            using var record = JsonDocument.Parse(line);
            records.Add(new RawRecord(record.RootElement.GetProperty("cveMetadata").GetProperty("cveId").GetString()!, line, "application/json"));
        }

        return records;
    }
}
