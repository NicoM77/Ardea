using System.Text.Json;
using Ardea.Catalog.Contracts;
using Ardea.Catalog.Persistence;
using Ardea.Catalog.Products;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Catalog.Learning;

/// <summary>
/// Decisions about learned entries inside the caller's unit of work (master plan 6.9 steps 5 and 6, task 4.10): a
/// proposal takes effect, is rejected, or an entry in effect is withdrawn. Every decision is checked against the
/// catalogue in effect again, writes an event for the measures, and has the entries concerned computed again. Nothing
/// here touches a curated row: a learned entry is a new row, or a raw key's mapping.
/// </summary>
internal sealed class LearnedEntries(CatalogDbContext db, ProductCatalog catalog, IEnumerable<ICatalogChangeSubscriber> subscribers, TimeProvider time)
{

    /// <summary>Every key of <c>catalog.product</c>, also of withdrawn learned entries: a key is never given twice.</summary>
    public async Task<HashSet<string>> KeysAsync(CancellationToken cancellationToken) =>
        (await db.Products.AsNoTracking().Select(p => p.Key).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The proposal takes effect: the raw key becomes an alias of an entry in effect, or a new learned entry. Returns the
    /// rule that failed, or <c>null</c>. <paramref name="actor"/> is the administrator who confirmed it, <c>null</c> for
    /// the system.
    /// </summary>
    /// <param name="row">The raw key.</param>
    /// <param name="proposal">What takes effect.</param>
    /// <param name="actor">The administrator, or <c>null</c> for the system.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="layers">The catalogue to check against; by default read anew in this unit of work.</param>
    /// <param name="keys">Every key of <c>catalog.product</c>; by default read anew.</param>
    public async Task<string?> AdoptAsync(
        UnknownProductRow row, LearnedProposal proposal, Guid? actor, CancellationToken cancellationToken, CatalogLayers? layers = null, ISet<string>? keys = null)
    {
        layers ??= await catalog.ReadLayersAsync(cancellationToken);
        var now = time.GetUtcNow();
        string key;
        IReadOnlyList<string> cpe = [], packages = [];
        switch (proposal.Decision)
        {
            case Decisions.Alias when proposal.AliasOf is { } target:
                if (layers.FindByKey(target) is null)
                {
                    return "unknown_key";
                }

                key = target;
                row.Outcome = UnknownProductRow.Alias;
                break;

            case Decisions.New when proposal.NewProduct is { } product:
                if (ProductDrafts.Problem(product, layers, keys ?? await KeysAsync(cancellationToken), allowedCpe: null, allowedRelated: null) is { } problem)
                {
                    return problem;
                }

                db.Products.Add(new ProductRow
                {
                    Key = product.Key,
                    Name = product.Name.Trim(),
                    Vendor = product.Vendor?.Trim(),
                    Kind = product.Kind,
                    CpePairs = [.. product.Cpe],
                    Purls = [.. product.Packages],
                    Aliases = JsonSerializer.Serialize(product.Aliases, LearnedProposal.Json),
                    VersionScheme = product.VersionScheme,
                    PlatformKey = product.Platform,
                    RelatedKeys = [.. product.Related],
                    Origin = ProductRow.Learned,
                    State = ProductRow.Active,
                    Confidence = proposal.Confidence,
                    Reasoning = proposal.Reasoning,
                    Evidence = JsonSerializer.Serialize(new { rawKey = row.RawKey, vendor = row.Vendor, product = row.Product, cpe = row.CpePairs, purl = row.Purl, records = row.RecordCount }, LearnedProposal.Json),
                    LearnedBy = proposal.LearnedBy,
                    DecidedBy = actor,
                    DecidedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                key = product.Key;
                cpe = product.Cpe;
                packages = product.Packages;
                row.Outcome = UnknownProductRow.New;
                break;

            default:
                return "decision";
        }

        row.ProductKey = key;
        row.Proposal = proposal.ToJson();
        row.DecidedBy = actor;
        row.DecidedAt = now;
        Event(actor is null ? LearningEventRow.Adopted : LearningEventRow.Confirmed, row.RawKey, key, actor, now);
        await db.SaveChangesAsync(cancellationToken);
        await NotifyAsync([row.RawKey], [], cpe, packages, cancellationToken);
        return null;
    }

    /// <summary>A proposal, or a raw key nobody could map, waits for a person; nothing takes effect.</summary>
    public async Task HoldAsync(UnknownProductRow row, LearnedProposal proposal, CancellationToken cancellationToken)
    {
        row.Outcome = proposal.Decision == Decisions.Unclear ? UnknownProductRow.Unclear : UnknownProductRow.Pending;
        row.Proposal = proposal.ToJson();
        row.ProductKey = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Rejected: never proposed again. Only what is not in effect can be rejected; an entry in effect is withdrawn.</summary>
    public async Task<string?> RejectAsync(UnknownProductRow row, Guid actor, CancellationToken cancellationToken)
    {
        if (row.Outcome is not (UnknownProductRow.Open or UnknownProductRow.Pending or UnknownProductRow.Unclear))
        {
            return "state";
        }

        var now = time.GetUtcNow();
        row.Outcome = UnknownProductRow.Rejected;
        row.ProductKey = null;
        row.DecidedBy = actor;
        row.DecidedAt = now;
        Event(LearningEventRow.Rejected, row.RawKey, null, actor, now);
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    /// <summary>
    /// Takes an entry in effect back (task 4.10): an alias stops mapping its raw key; a new learned entry is withdrawn
    /// together with the aliases that point to it. The entries concerned are computed again; what the curated catalogue
    /// found before is what they find again.
    /// </summary>
    public async Task<string?> WithdrawAsync(UnknownProductRow row, Guid? actor, CancellationToken cancellationToken)
    {
        if (row.Outcome is not (UnknownProductRow.Alias or UnknownProductRow.New) || row.ProductKey is not { } key)
        {
            return "state";
        }

        var now = time.GetUtcNow();
        var rows = new List<UnknownProductRow> { row };
        IReadOnlyList<string> productKeys = [], cpe = [], packages = [];
        if (row.Outcome == UnknownProductRow.New
            && await db.Products.SingleOrDefaultAsync(p => p.Key == key && p.Origin == ProductRow.Learned, cancellationToken) is { } product)
        {
            product.State = ProductRow.Withdrawn;
            product.DecidedBy = actor;
            product.DecidedAt = now;
            product.UpdatedAt = now;
            rows.AddRange(await db.UnknownProducts.Where(u => u.ProductKey == key && u.Outcome == UnknownProductRow.Alias && u.Id != row.Id).ToListAsync(cancellationToken));
            productKeys = [key];
            cpe = product.CpePairs;
            packages = product.Purls;
        }

        foreach (var withdrawn in rows)
        {
            withdrawn.Outcome = UnknownProductRow.Withdrawn;
            withdrawn.DecidedBy = actor;
            withdrawn.DecidedAt = now;
            Event(LearningEventRow.Withdrawn, withdrawn.RawKey, withdrawn.ProductKey, actor, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await NotifyAsync([.. rows.Select(r => r.RawKey)], productKeys, cpe, packages, cancellationToken);
        return null;
    }

    public void Event(string kind, string? rawKey, string? productKey, Guid? actor, DateTimeOffset at, int calls = 0, int tokens = 0, object? data = null) =>
        db.LearningEvents.Add(new LearningEventRow
        {
            Id = Guid.CreateVersion7(),
            At = at,
            Kind = kind,
            RawKey = rawKey,
            ProductKey = productKey,
            Actor = actor,
            Calls = calls,
            Tokens = tokens,
            Data = data is null ? null : JsonSerializer.Serialize(data, LearnedProposal.Json),
        });

    private async Task NotifyAsync(IReadOnlyList<string> rawKeys, IReadOnlyList<string> productKeys, IReadOnlyList<string> cpe, IReadOnlyList<string> packages, CancellationToken cancellationToken)
    {
        var change = new LearnedEntriesChanged(rawKeys, productKeys, cpe, packages);
        foreach (var subscriber in subscribers)
        {
            await subscriber.OnLearnedEntriesChangedAsync(change, cancellationToken);
        }
    }
}
