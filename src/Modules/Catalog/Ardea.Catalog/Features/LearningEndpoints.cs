using System.Text.Json;
using Ardea.Catalog.Learning;
using Ardea.Catalog.Persistence;
using Ardea.Catalog.Products;
using Ardea.Platform.Ai;
using Ardea.Platform.Audit;
using Ardea.Platform.Http;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Settings;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Catalog.Features;

/// <summary>
/// The catalogue that learns, for the instance administrator (master plan 6.9, 11.4; tasks 4.9, 4.10): the review list
/// next to the quarantine on the page «Sources» with confirm, change and reject; withdrawal of an entry in effect; the
/// export in the format of the curated files; the measures; the provider the catalogue asks.
/// </summary>
internal static class LearningEndpoints
{
    private static readonly string[] Views = ["review", "open", "in_effect", "decided"];

    public static void Map(IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/instance/catalog").WithTags("Catalog").RequireAuthorization(Policies.InstanceAdmin);
        admin.MapGet("/learning", StatusAsync).WithName("getCatalogLearning").Produces<LearningStatusView>();
        admin.MapPatch("/learning", UpdateAsync).WithName("updateCatalogLearning").Produces<LearningStatusView>().Problems(400, 409);
        admin.MapPost("/learning/run", RunAsync).WithName("runCatalogLearning").Produces(StatusCodes.Status202Accepted);
        admin.MapGet("/learned", ListAsync).WithName("listLearnedEntries").Produces<IReadOnlyList<LearnedItemView>>().Problems(400);
        admin.MapGet("/learned/export", ExportAsync).WithName("exportLearnedEntries").Produces(StatusCodes.Status200OK, contentType: "application/zip");
        admin.MapPost("/learned/{id:guid}/adopt", AdoptAsync).WithName("adoptLearnedEntry").Produces<LearnedItemView>().Problems(400, 404, 409);
        admin.MapPost("/learned/{id:guid}/reject", RejectAsync).WithName("rejectLearnedEntry").NoContent().Problems(404, 409);
        admin.MapPost("/learned/{id:guid}/withdraw", WithdrawAsync).WithName("withdrawLearnedEntry").NoContent().Problems(404, 409);
    }

    private static async Task<IResult> StatusAsync(
        CatalogDbContext db, IInstanceSettings settings, IAiProviders providers, LearningOptions options, TimeProvider time, CancellationToken cancellationToken) =>
        Results.Ok(await ReadStatusAsync(db, settings, providers, options, time, cancellationToken));

    internal static async Task<LearningStatusView> ReadStatusAsync(
        CatalogDbContext db, IInstanceSettings settings, IAiProviders providers, LearningOptions options, TimeProvider time, CancellationToken cancellationToken)
    {
        var setting = await settings.GetAsync(LearningOptions.Provider, cancellationToken);
        var provider = LearningOptions.Choose(setting.Value, providers.All);
        var now = time.GetUtcNow();
        var day = AiLimits.DayStart(now);
        var calls = await db.LearningEvents.Where(e => e.Kind == LearningEventRow.Asked && e.At >= day).SumAsync(e => e.Calls, cancellationToken);
        var outcomes = await db.UnknownProducts.GroupBy(u => u.Outcome).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var collected = await db.LearningEvents.AsNoTracking().Where(e => e.Kind == LearningEventRow.Collected).OrderByDescending(e => e.At).FirstOrDefaultAsync(cancellationToken);
        int? productKeys = null, unknown = null;
        if (collected?.Data is { } data)
        {
            using var json = JsonDocument.Parse(data);
            productKeys = json.RootElement.TryGetProperty("productKeys", out var p) ? p.GetInt32() : null;
            unknown = json.RootElement.TryGetProperty("unknown", out var u) ? u.GetInt32() : null;
        }

        // Eight weeks, Monday to Monday in UTC, the current one last.
        var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        var since = monday.AddDays(-7 * 7);
        var events = await db.LearningEvents.AsNoTracking()
            .Where(e => e.At >= since && e.Kind != LearningEventRow.Collected && e.Kind != LearningEventRow.Asked)
            .Select(e => new { e.Kind, e.At }).ToListAsync(cancellationToken);
        var weeks = Enumerable.Range(0, 8).Select(i =>
        {
            var start = since.AddDays(7 * i);
            var inWeek = events.Where(e => e.At >= start && e.At < start.AddDays(7)).ToList();
            int Count(string kind) => inWeek.Count(e => e.Kind == kind);
            return new LearningWeekView(
                DateOnly.FromDateTime(start.UtcDateTime), Count(LearningEventRow.Learned), Count(LearningEventRow.Adopted), Count(LearningEventRow.Confirmed),
                Count(LearningEventRow.Rejected), Count(LearningEventRow.Withdrawn));
        }).ToList();

        return new LearningStatusView(
            new LearningProviderView(setting.Source == SettingSource.Default ? null : setting.Value, setting.Locked, provider?.Name, provider?.Local ?? false, provider?.Model),
            calls, options.DailyCalls, productKeys, unknown,
            productKeys is > 0 && unknown is { } n ? Math.Round((double)n / productKeys.Value, 4) : null,
            collected?.At, outcomes, weeks);
    }

    private static async Task<IResult> UpdateAsync(
        UpdateLearningRequest request, CatalogDbContext db, IInstanceSettings settings, IAiProviders providers, LearningOptions options, IAuditLog audit,
        IUnitOfWork unitOfWork, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (request.Provider is { Length: > 0 } name && name != LearningOptions.NoProvider && !providers.All.Any(p => p.Name == name))
        {
            return ApiProblems.Validation("validation_failed", "No such AI provider is configured.", new Dictionary<string, string[]> { ["provider"] = ["Choose a configured provider or none."] });
        }

        if (request.Provider is { } value)
        {
            if ((await settings.GetAsync(LearningOptions.Provider, cancellationToken)).Locked)
            {
                return ApiProblems.Problem("setting_locked", StatusCodes.Status409Conflict, "The catalogue's provider is set by an environment variable and cannot be changed here.");
            }

            await settings.SetAsync(LearningOptions.Provider, value.Length == 0 ? null : value.Trim(), tenant.UserId, cancellationToken);
            await audit.RecordAsync(
                new AuditEntry("instance.catalog_provider_changed", "setting", LearningOptions.Provider.Key, new { provider = value.Length == 0 ? null : value.Trim() }) { InstanceEvent = true },
                cancellationToken);
        }

        var status = await ReadStatusAsync(db, settings, providers, options, time, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(status);
    }

    /// <summary>Starts the job now, e.g. after the first fill; a waiting run is not doubled.</summary>
    private static async Task<IResult> RunAsync(IJobQueue jobs, IAuditLog audit, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        await jobs.EnqueueAsync(new GrowCatalog(), GrowCatalog.Options, cancellationToken);
        await audit.RecordAsync(new AuditEntry("instance.catalog_learning_started") { InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Accepted();
    }

    private static async Task<IResult> ListAsync(string? view, string? rawKey, int? limit, CatalogDbContext db, ProductCatalog catalog, CancellationToken cancellationToken)
    {
        view ??= "review";
        if (!Views.Contains(view, StringComparer.Ordinal))
        {
            return ApiProblems.Validation("validation_failed", "Unknown view.", new Dictionary<string, string[]> { ["view"] = ["Use review, open, in_effect or decided."] });
        }

        var rows = db.UnknownProducts.AsNoTracking();
        if (rawKey is { Length: > 0 } key)
        {
            // The link from a hit names the raw key, whatever became of it since.
            rows = rows.Where(u => u.RawKey == key);
        }
        else
        {
            rows = view switch
            {
                "review" => rows.Where(u => u.Outcome == UnknownProductRow.Pending || u.Outcome == UnknownProductRow.Unclear),
                "open" => rows.Where(u => u.Outcome == UnknownProductRow.Open),
                "in_effect" => rows.Where(u => u.Outcome == UnknownProductRow.Alias || u.Outcome == UnknownProductRow.New),
                _ => rows.Where(u => u.Outcome == UnknownProductRow.Rejected || u.Outcome == UnknownProductRow.Withdrawn),
            };
        }

        var ordered = view is "in_effect" or "decided"
            ? rows.OrderByDescending(u => u.DecidedAt).ThenBy(u => u.RawKey)
            : rows.OrderByDescending(u => u.InKev).ThenByDescending(u => u.RecordCount).ThenBy(u => u.RawKey);
        var list = await ordered.Take(Math.Clamp(limit ?? 100, 1, 500)).ToListAsync(cancellationToken);
        var layers = await catalog.LayersAsync(cancellationToken);
        return Results.Ok(list.Select(r => LearningViewMapping.View(r, k => layers.FindByKey(k)?.Name)).ToList());
    }

    /// <summary>
    /// Confirm, change or map by hand. The decision is checked against the catalogue of now as a proposal of the model
    /// is: an entry that exists, or a new product whose key and spellings no entry has.
    /// </summary>
    private static async Task<IResult> AdoptAsync(
        Guid id, AdoptLearnedRequest? request, CatalogDbContext db, ProductCatalog catalog, LearnedEntries entries, CatalogCache cache, IAuditLog audit,
        IUnitOfWork unitOfWork, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var row = await db.UnknownProducts.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (row is null)
        {
            return ApiProblems.Problem("not_found", StatusCodes.Status404NotFound, "No such raw key.");
        }

        if (row.Outcome is not (UnknownProductRow.Open or UnknownProductRow.Pending or UnknownProductRow.Unclear))
        {
            return ApiProblems.Problem("learned_entry_decided", StatusCodes.Status409Conflict, "This raw key is already decided.");
        }

        var stored = LearnedProposal.Read(row.Proposal);
        var layers = await catalog.ReadLayersAsync(cancellationToken);
        var keys = await entries.KeysAsync(cancellationToken);
        LearnedProposal proposal;
        if (request?.AliasOf is { Length: > 0 } alias)
        {
            proposal = new LearnedProposal(Decisions.Alias, alias.Trim(), null, "high", stored?.Reasoning, LearnedBy.Admin, stored?.Provider, stored?.Model, stored?.PromptVersion);
        }
        else if (request?.NewProduct is { } input)
        {
            var name = input.Name?.Trim() ?? string.Empty;
            var vendor = string.IsNullOrWhiteSpace(input.Vendor) ? null : input.Vendor.Trim();
            var key = ProductDrafts.KeyFor(name, vendor, k => keys.Contains(k) || layers.FindByKey(k) is not null);
            if (key is null)
            {
                return Invalid("key");
            }

            var statement = new Contracts.UnknownProductStatement(row.RawKey, row.Vendor, row.Product, row.CpePairs, row.Purl, row.RecordCount, row.InKev);
            var product = new ProposedProduct(
                key, name, vendor, input.Kind ?? string.Empty, input.VersionScheme ?? string.Empty,
                [.. (input.Cpe ?? []).Select(c => ProductDrafts.CpeText(c.Trim()) ?? c).Distinct(StringComparer.Ordinal)],
                Contracts.PackageCoordinate.TryParsePurl(row.Purl, out var package, out _) ? [package.ToString()] : [],
                RuleMapping.RecordSpelling(statement, name, vendor), [.. (input.Related ?? []).Distinct(StringComparer.Ordinal)],
                string.IsNullOrWhiteSpace(input.Platform) ? null : input.Platform.Trim());
            proposal = new LearnedProposal(Decisions.New, null, product, "high", stored?.Reasoning, LearnedBy.Admin, stored?.Provider, stored?.Model, stored?.PromptVersion);
        }
        else if (stored is { Decision: Decisions.Alias or Decisions.New })
        {
            proposal = stored with { Hold = null };
        }
        else
        {
            return ApiProblems.Validation("validation_failed", "Name the entry or describe the new product.", new Dictionary<string, string[]> { ["aliasOf"] = ["Name an entry of the catalogue or describe a new product."] });
        }

        if (await entries.AdoptAsync(row, proposal, tenant.UserId, cancellationToken, layers, keys) is { } problem)
        {
            return Invalid(problem);
        }

        await audit.RecordAsync(new AuditEntry("instance.learned_entry_adopted", "unknown_product", row.RawKey, new { productKey = row.ProductKey, proposal.Decision }) { InstanceEvent = true }, cancellationToken);
        var view = LearningViewMapping.View(row, k => k == proposal.NewProduct?.Key ? proposal.NewProduct.Name : layers.FindByKey(k)?.Name);
        await unitOfWork.CommitAsync(cancellationToken);
        cache.Invalidate();
        return Results.Ok(view);
    }

    private static async Task<IResult> RejectAsync(
        Guid id, CatalogDbContext db, LearnedEntries entries, IAuditLog audit, IUnitOfWork unitOfWork, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var row = await db.UnknownProducts.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (row is null)
        {
            return ApiProblems.Problem("not_found", StatusCodes.Status404NotFound, "No such raw key.");
        }

        if (await entries.RejectAsync(row, tenant.UserId ?? Guid.Empty, cancellationToken) is not null)
        {
            return ApiProblems.Problem("learned_entry_decided", StatusCodes.Status409Conflict, "An entry in effect is withdrawn, not rejected.");
        }

        await audit.RecordAsync(new AuditEntry("instance.learned_entry_rejected", "unknown_product", row.RawKey) { InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>Takes a learned entry in effect back with one click (master plan 6.9); the entries concerned are computed again.</summary>
    private static async Task<IResult> WithdrawAsync(
        Guid id, CatalogDbContext db, LearnedEntries entries, CatalogCache cache, IAuditLog audit, IUnitOfWork unitOfWork, ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        var row = await db.UnknownProducts.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (row is null)
        {
            return ApiProblems.Problem("not_found", StatusCodes.Status404NotFound, "No such raw key.");
        }

        var productKey = row.ProductKey;
        if (await entries.WithdrawAsync(row, tenant.UserId, cancellationToken) is not null)
        {
            return ApiProblems.Problem("learned_entry_not_in_effect", StatusCodes.Status409Conflict, "Only a learned entry in effect can be withdrawn.");
        }

        await audit.RecordAsync(new AuditEntry("instance.learned_entry_withdrawn", "unknown_product", row.RawKey, new { productKey }) { InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        cache.Invalidate();
        return Results.NoContent();
    }

    /// <summary>The learned entries in effect as curated files (master plan 6.9 «Zurück ins Projekt»), one per vendor, in a zip.</summary>
    private static async Task<IResult> ExportAsync(CatalogDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var bytes = await CatalogExport.WriteAsync(db, time.GetUtcNow(), cancellationToken);
        return Results.File(bytes, "application/zip", $"ardea-learned-catalog-{time.GetUtcNow():yyyy-MM-dd}.zip");
    }

    private static IResult Invalid(string rule) =>
        ApiProblems.Validation("learned_entry_invalid", "The entry does not pass the catalogue's check.", new Dictionary<string, string[]> { [rule] = [Messages.GetValueOrDefault(rule, "Check the values.")] });

    private static readonly Dictionary<string, string> Messages = new(StringComparer.Ordinal)
    {
        ["key"] = "The name gives no free key; choose another name.",
        ["unknown_key"] = "No entry in effect has this key.",
        ["name"] = "Enter a name of at most 100 characters, without markup.",
        ["kind"] = "Choose a kind.",
        ["version_scheme"] = "Choose a version scheme.",
        ["name_taken"] = "An entry already has this name or spelling.",
        ["cpe"] = "A CPE pair is not vendor:product, or an entry already has it.",
        ["package"] = "An entry already has this package.",
        ["related"] = "A related product or the platform is not an entry of the catalogue.",
        ["platform"] = "A plugin names its platform.",
    };
}
