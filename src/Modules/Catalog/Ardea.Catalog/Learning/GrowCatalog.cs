using Ardea.Catalog.Contracts;
using Ardea.Catalog.Persistence;
using Ardea.Catalog.Products;
using Ardea.Platform.Ai;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ardea.Catalog.Learning;

/// <summary>
/// The catalogue grows by itself (master plan 6.9): once a day, after a source's first load, on request of the
/// administrator, and continued by itself while keys and calls are left. <see cref="Collect"/> is <c>false</c> for a
/// continuation, which skips step 1; <see cref="RulesAfter"/> is the raw key the mapping without a model goes on after.
/// </summary>
internal sealed record GrowCatalog(bool Collect = true, string? RulesAfter = null) : IJob
{
    public static string Kind => "catalog.grow";

    public static EnqueueOptions Options => new() { CoalesceKey = "catalog.grow", Priority = JobPriority.Background };
}

/// <summary>
/// The steps of master plan 6.9, each in a unit of work of its own (ADR 023): collect the unknown products with their
/// records; map without a model what rules can map; then, with a provider and calls left, ask the model about one key
/// after the other, most frequent first: read in a short unit of work, call without an open transaction, write the
/// outcome in another. The job's own unit of work stays unused, so no transaction is open while the model thinks.
/// The mapping without a model works in batches of its own units of work, so a run that ends early keeps what it did;
/// after <see cref="LearningOptions.RuleBatchesPerRun"/> batches the run hands the rest to a continuation, before the
/// model is asked. The kind has one attempt: a repeated run would ask the model again.
/// </summary>
internal sealed partial class GrowCatalogHandler(
    IUnitOfWorkFactory units, Learner learner, CatalogCache cache, IAiProviders providers, LearningOptions options, TimeProvider time,
    ILogger<GrowCatalogHandler> logger)
    : IJobHandler<GrowCatalog>
{
    public async Task HandleAsync(GrowCatalog job, JobContext context, CancellationToken cancellationToken)
    {
        if (job.Collect)
        {
            await CollectAsync(cancellationToken);
        }

        var (mapped, rulesAfter) = await MapByRulesAsync(job.RulesAfter, cancellationToken);
        var (asked, more) = rulesAfter is not null || context.Attempt > 1 ? (0, false) : await AskModelAsync(cancellationToken);
        if (rulesAfter is not null || more)
        {
            await using var scope = units.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IJobQueue>().EnqueueAsync(new GrowCatalog(Collect: false, rulesAfter), GrowCatalog.Options, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        }

        LogRun(mapped, asked, rulesAfter is not null || more);
    }

    /// <summary>
    /// Step 1: every product key of the entries the catalogue does not know, with its records and KEV listing. New keys
    /// are added as open; for known ones only the counts and the statement change, never a decision.
    /// </summary>
    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        await using var scope = units.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<CatalogDbContext>();
        var census = await services.GetRequiredService<IUnknownProductFeed>().CollectAsync(cancellationToken);
        var rows = await db.UnknownProducts.ToDictionaryAsync(u => u.RawKey, StringComparer.Ordinal, cancellationToken);
        var now = time.GetUtcNow();
        foreach (var statement in census.Unknown.Where(s => s.RawKey.Length <= 200))
        {
            if (!rows.TryGetValue(statement.RawKey, out var row))
            {
                row = db.UnknownProducts.Add(new UnknownProductRow
                {
                    Id = Guid.CreateVersion7(),
                    RawKey = statement.RawKey,
                    Product = Cut(statement.Product),
                    Outcome = UnknownProductRow.Open,
                    FirstSeenAt = now,
                }).Entity;
            }

            row.Vendor = statement.Vendor is null ? null : Cut(statement.Vendor);
            row.Product = Cut(statement.Product);
            if (!row.CpePairs.AsSpan().SequenceEqual([.. statement.CpePairs]))
            {
                row.CpePairs = [.. statement.CpePairs];
            }

            row.Purl = statement.Purl is { Length: <= 500 } purl ? purl : null;
            row.RecordCount = statement.RecordCount;
            row.InKev = statement.InKev;
        }

        var unknown = census.Unknown.Count;
        services.GetRequiredService<LearnedEntries>().Event(
            LearningEventRow.Collected, null, null, null, now, data: new { productKeys = census.ProductKeys, unknown, records = census.Unknown.Sum(u => u.RecordCount) });
        await db.SaveChangesAsync(cancellationToken);
        await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        LogCollected(census.ProductKeys, unknown);
    }

    /// <summary>
    /// Step 2, without a model: what rules map takes effect at once; a suspicious statement waits for a person. Keys in
    /// raw key order, a batch per unit of work; each batch checks against the catalogue as the batches before left it,
    /// and a spelling two keys of one batch would take is left to the next run. Returns the keys mapped and, when the run's
    /// batches are used up, the raw key to go on after.
    /// </summary>
    private async Task<(int Mapped, string? After)> MapByRulesAsync(string? after, CancellationToken cancellationToken)
    {
        var mapped = 0;
        for (var batch = 0; batch < options.RuleBatchesPerRun; batch++)
        {
            var (changed, last, count) = await MapBatchAsync(after, cancellationToken);
            mapped += changed;
            if (count < options.RuleBatchSize || last is null)
            {
                return (mapped, null);
            }

            after = last;
        }

        return (mapped, after);
    }

    private async Task<(int Changed, string? Last, int Count)> MapBatchAsync(string? after, CancellationToken cancellationToken)
    {
        var changed = 0;
        string? last;
        int count;
        await using (var scope = units.CreateScope())
        {
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<CatalogDbContext>();
            var entries = services.GetRequiredService<LearnedEntries>();
            var rows = await Due(db.UnknownProducts)
                .Where(u => u.Proposal == null && (after == null || string.Compare(u.RawKey, after) > 0))
                .OrderBy(u => u.RawKey)
                .Take(options.RuleBatchSize)
                .ToListAsync(cancellationToken);
            (last, count) = (rows.LastOrDefault()?.RawKey, rows.Count);
            if (rows.Count == 0)
            {
                return (0, null, 0);
            }

            var layers = await services.GetRequiredService<ProductCatalog>().ReadLayersAsync(cancellationToken);
            var keys = await entries.KeysAsync(cancellationToken);
            var taken = new HashSet<string>(StringComparer.Ordinal);
            var now = time.GetUtcNow();
            foreach (var row in rows)
            {
                if (RuleMapping.Map(Statement(row), layers, keys) is not { } proposal)
                {
                    continue;
                }

                if (proposal.NewProduct is { } product
                    && !new[] { product.Name }.Concat(product.Cpe).Concat(product.Packages).All(t => taken.Add(ProductKey.NormalizeProduct(t) ?? t)))
                {
                    continue;
                }

                if (ProductDrafts.Suspicious(row.Vendor, row.Product))
                {
                    entries.Event(LearningEventRow.Learned, row.RawKey, proposal.AliasOf ?? proposal.NewProduct?.Key, null, now);
                    await entries.HoldAsync(row, proposal with { Hold = Holds.SuspiciousInput }, cancellationToken);
                    LearnMetrics.Keys.Add(1, new KeyValuePair<string, object?>("outcome", "pending"));
                    continue;
                }

                if (await entries.AdoptAsync(row, proposal, actor: null, cancellationToken, layers, keys) is null)
                {
                    entries.Event(LearningEventRow.Learned, row.RawKey, proposal.AliasOf ?? proposal.NewProduct?.Key, null, now);
                    changed++;
                    if (proposal.NewProduct is { } p)
                    {
                        keys.Add(p.Key);
                    }

                    LearnMetrics.Keys.Add(1, new KeyValuePair<string, object?>("outcome", "rule"));
                }
            }

            // The last event is not written by an adoption's own save: save before the commit.
            await db.SaveChangesAsync(cancellationToken);
            await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        }

        // Every process reads what this one wrote at its next unit of work (ADR 040); this one at once.
        cache.Invalidate();
        return (changed, last, count);
    }

    /// <summary>
    /// Step 3 to 5 with the model: the most frequent open keys first, KEV before all, keys that failed on a provider
    /// before today again. Returns the keys asked about and whether keys and calls are left for a continuation.
    /// </summary>
    private async Task<(int Asked, bool More)> AskModelAsync(CancellationToken cancellationToken)
    {
        AiProviderInfo? provider;
        int callsLeft;
        List<Guid> due;
        var day = AiLimits.DayStart(time.GetUtcNow());
        if (providers.All.Count == 0)
        {
            // Without a provider the mapping without a model runs, and nothing else (master plan 6.9 «Ohne KI»).
            return (0, false);
        }

        await using (var scope = units.CreateScope())
        {
            var services = scope.ServiceProvider;
            provider = LearningOptions.Choose((await services.GetRequiredService<IInstanceSettings>().GetAsync(LearningOptions.Provider, cancellationToken)).Value, providers.All);
            if (provider is null)
            {
                return (0, false);
            }

            var db = services.GetRequiredService<CatalogDbContext>();
            var used = await db.LearningEvents.Where(e => e.Kind == LearningEventRow.Asked && e.At >= day).SumAsync(e => e.Calls, cancellationToken);
            callsLeft = options.DailyCalls - used;
            due = await Due(db.UnknownProducts)
                .Where(u => u.LastAttemptAt == null || u.LastAttemptAt < day)
                .OrderByDescending(u => u.InKev).ThenByDescending(u => u.RecordCount).ThenBy(u => u.RawKey)
                .Select(u => u.Id)
                .Take(LearningOptions.KeysPerRun + 1)
                .ToListAsync(cancellationToken);
        }

        var asked = 0;
        foreach (var id in due.Take(LearningOptions.KeysPerRun))
        {
            if (callsLeft <= 0)
            {
                break;
            }

            var calls = await AskAsync(id, provider, callsLeft, cancellationToken);
            callsLeft -= calls;
            asked++;
        }

        return (asked, callsLeft > 0 && due.Count > asked && asked > 0);
    }

    /// <summary>One key: read, call, write (ADR 023). Returns the calls made.</summary>
    private async Task<int> AskAsync(Guid id, AiProviderInfo provider, int callsLeft, CancellationToken cancellationToken)
    {
        LearnInput input;
        CatalogLayers layers;
        HashSet<string> keys;
        await using (var scope = units.CreateScope())
        {
            var services = scope.ServiceProvider;
            var row = await services.GetRequiredService<CatalogDbContext>().UnknownProducts.AsNoTracking().SingleAsync(u => u.Id == id, cancellationToken);
            var titles = await services.GetRequiredService<IUnknownProductFeed>().TitlesAsync(row.RawKey, LearnInput.TitleCount, cancellationToken);
            layers = await services.GetRequiredService<ProductCatalog>().LayersAsync(cancellationToken);
            keys = await services.GetRequiredService<LearnedEntries>().KeysAsync(cancellationToken);
            input = LearnInput.Build(Statement(row), titles, layers);
        }

        var result = await learner.LearnAsync(input, provider, layers, keys, callsLeft, cancellationToken);
        if (result.Attempts == 0)
        {
            return 0;
        }

        await using (var scope = units.CreateScope())
        {
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<CatalogDbContext>();
            var entries = services.GetRequiredService<LearnedEntries>();
            var now = time.GetUtcNow();
            var row = await db.UnknownProducts.SingleAsync(u => u.Id == id, cancellationToken);
            row.LastAttemptAt = now;
            row.Attempts += result.Attempts;
            entries.Event(LearningEventRow.Asked, row.RawKey, null, null, now, result.Attempts, result.Tokens);
            var outcome = "failed";

            // Someone may have decided while the model thought; a decision of a person stands.
            if (row.Outcome == UnknownProductRow.Open)
            {
                var proposal = result.Proposal;
                // Two answers that failed the check: on the review list. A single one (the day's last call) is asked again tomorrow.
                if (proposal is null && result.Failure == Holds.InvalidOutput && result.Attempts >= Learner.Attempts)
                {
                    proposal = new LearnedProposal(Decisions.Unclear, null, null, "low", null, LearnedBy.Model, provider.Name, provider.Model, LearnPrompt.Version, Holds.InvalidOutput);
                }

                if (proposal is not null)
                {
                    if (proposal.Decision != Decisions.Unclear)
                    {
                        entries.Event(LearningEventRow.Learned, row.RawKey, proposal.AliasOf ?? proposal.NewProduct?.Key, null, now);
                    }

                    // Step 5: in effect at once only with high confidence, a checked answer and an inconspicuous statement.
                    var hold = proposal.Decision == Decisions.Unclear ? proposal.Hold ?? Holds.Unclear
                        : input.Suspicious ? Holds.SuspiciousInput
                        : proposal.Confidence != "high" ? Holds.Confidence
                        : null;
                    if (hold is null && await entries.AdoptAsync(row, proposal, actor: null, cancellationToken) is null)
                    {
                        outcome = "adopted";
                    }
                    else
                    {
                        // Held, or the write's own check against the catalogue of now found what the model's did not.
                        await entries.HoldAsync(row, proposal with { Hold = hold ?? Holds.Check }, cancellationToken);
                        outcome = row.Outcome;
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
            LearnMetrics.Keys.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
            LogAsked(provider.Name, result.Attempts, result.Tokens, outcome, result.DurationMs);
        }

        cache.Invalidate();
        return result.Attempts;
    }

    /// <summary>Open keys from <see cref="LearningOptions.MinRecords"/> records on, or at once when one record is in KEV (master plan 6.9 step 1).</summary>
    private IQueryable<UnknownProductRow> Due(IQueryable<UnknownProductRow> rows) =>
        rows.Where(u => u.Outcome == UnknownProductRow.Open && (u.RecordCount >= options.MinRecords || u.InKev));

    private static UnknownProductStatement Statement(UnknownProductRow row) =>
        new(row.RawKey, row.Vendor, row.Product, row.CpePairs, row.Purl, row.RecordCount, row.InKev);

    private static string Cut(string value) => value.Length <= 200 ? value : value[..200];

    [LoggerMessage(EventId = 2103, Level = LogLevel.Information, Message = "Catalogue: {ProductKeys} product keys in the entries, {Unknown} unknown")]
    private partial void LogCollected(int productKeys, int unknown);

    [LoggerMessage(EventId = 2104, Level = LogLevel.Information, Message = "Catalogue: asked AI provider {Provider} about a key, {Attempts} calls, {Tokens} tokens, outcome {Outcome}, {DurationMs} ms")]
    private partial void LogAsked(string provider, int attempts, int tokens, string outcome, long durationMs);

    [LoggerMessage(EventId = 2105, Level = LogLevel.Information, Message = "Catalogue grew: {Mapped} keys mapped by rules, {Asked} asked of the model, continues: {More}")]
    private partial void LogRun(int mapped, int asked, bool more);
}

/// <summary>Queues a run of <see cref="GrowCatalog"/> in the caller's unit of work, e.g. after a source's first load (master plan 6.9).</summary>
internal sealed class CatalogGrowth(IJobQueue jobs) : ICatalogGrowth
{
    public Task RequestAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        jobs.EnqueueAsync(new GrowCatalog(), GrowCatalog.Options with { Delay = delay }, cancellationToken);
}
