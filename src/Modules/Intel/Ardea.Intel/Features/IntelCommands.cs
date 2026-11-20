using Ardea.Intel.Correlation;
using Ardea.Intel.Persistence;
using Ardea.Intel.Sync;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Intel.Features;

internal static class IntelCommands
{
    public static readonly MaintenanceCommand Renormalize = new(
        "intel-renormalize",
        "intel renormalize --source <key>",
        "Translate every stored record of a source again with the current parser, without fetching",
        async (services, args, cancellationToken) =>
        {
            var index = Array.IndexOf(args, "--source");
            var key = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            var db = services.GetRequiredService<IntelDbContext>();
            var source = key is null ? null : await db.Sources.SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (source is null)
            {
                await Console.Error.WriteLineAsync("Usage: ardea intel renormalize --source <key>; known sources: " +
                    string.Join(", ", await db.Sources.Select(s => s.Key).OrderBy(k => k).ToListAsync(cancellationToken)));
                return 64;
            }

            var count = await db.SourceRecords.Where(r => r.SourceId == source.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.ParseStatus, "pending"), cancellationToken);
            await services.GetRequiredService<IJobQueue>().EnqueueAsync(new NormalizeRecords(source.Key), NormalizeRecords.Options(source.Key), cancellationToken);
            await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
            Console.WriteLine($"{count} record(s) of {source.Key} will be normalised again by the worker.");
            return 0;
        });

    public static readonly MaintenanceCommand ImportFull = new(
        "intel-import",
        "intel import --full [--source <key>]",
        "Load a source's whole inventory, by default every CVE record since 1999 (about 10 GB more)",
        async (services, args, cancellationToken) =>
        {
            var index = Array.IndexOf(args, "--source");
            var key = index >= 0 && index + 1 < args.Length ? args[index + 1] : "cve-program";
            var db = services.GetRequiredService<IntelDbContext>();
            var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (!args.Contains("--full") || source is null)
            {
                await Console.Error.WriteLineAsync("Usage: ardea intel import --full [--source <key>]; known sources: " +
                    string.Join(", ", await db.Sources.Select(s => s.Key).OrderBy(k => k).ToListAsync(cancellationToken)));
                return 64;
            }

            await services.GetRequiredService<IJobQueue>().EnqueueAsync(
                new SyncSource(source.Key, Full: true), new EnqueueOptions { CoalesceKey = $"intel.sync:{source.Key}" }, cancellationToken);
            await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
            Console.WriteLine($"The worker loads the whole inventory of {source.Key}; follow it on the «Sources» page or in the worker log.");
            return 0;
        });

    public static readonly MaintenanceCommand Recompute = new(
        "intel-recompute",
        "intel recompute",
        "Merge every entry again from its observations with the current rules and catalogue",
        async (services, args, cancellationToken) =>
        {
            var db = services.GetRequiredService<IntelDbContext>();
            var jobs = services.GetRequiredService<IJobQueue>();
            var keys = await db.EntryAliases.AsNoTracking()
                .Join(db.Entries.Where(e => e.State != "merged"), a => a.EntryId, e => e.Id, (a, e) => new { e.Id, Key = a.Scheme + ":" + a.Value })
                .GroupBy(x => x.Id).Select(g => g.Min(x => x.Key)!)
                .ToListAsync(cancellationToken);
            var warnings = await db.Entries.AsNoTracking().Where(e => e.Kind == "warning" && e.State != "merged").Select(e => "SRC:" + e.PrimaryKey).ToListAsync(cancellationToken);
            foreach (var key in keys.Concat(warnings))
            {
                await jobs.EnqueueAsync(new CorrelateEntry(key), CorrelateEntry.Options(key), cancellationToken);
            }

            await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
            Console.WriteLine($"{keys.Count + warnings.Count} entr(y/ies) will be merged again by the worker.");
            _ = args;
            return 0;
        });
}
