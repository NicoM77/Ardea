using Ardea.Assets.Contracts;
using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;
using Ardea.Platform.Audit;
using Ardea.Platform.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assets.Features;

/// <summary>
/// Where a technology runs, as an answer «affects us» states it (master plan 17.3, ADR 047). Writes like the component
/// endpoints: the checks of <see cref="ComponentRules"/>, the search keys, an audit entry and the project's reverse search.
/// </summary>
internal sealed class ProfilePlacement(AssetsDbContext db, IProductCatalog catalog, IAuditLog audit, IJobQueue jobs, TimeProvider time) : IProfilePlacement
{
    public async Task<PlacementResult> PlaceAsync(Guid projectId, Guid likeComponentId, string? version, CancellationToken cancellationToken)
    {
        var like = await db.Components.AsNoTracking().SingleOrDefaultAsync(c => c.Id == likeComponentId, cancellationToken);
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (like is null || project is null)
        {
            return new PlacementResult("not_found");
        }

        if (project.ArchivedAt is not null)
        {
            return new PlacementResult("archived");
        }

        // Two answers placing the same product in the same project take turns, so the second finds what the first added.
        var identity = ComponentValues.IdentityOf(like);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"assets.place:" + projectId + ":" + identity}, 0))", cancellationToken);

        var wanted = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        var same = (await db.Components.Where(c => c.ProjectId == projectId).OrderBy(c => c.Id).ToListAsync(cancellationToken))
            .Where(c => ComponentValues.IdentityOf(c) == identity).ToList();

        if (wanted is null)
        {
            var found = same.Find(c => c.Id == like.Id) ?? same.Find(c => c.VersionPrecision != VersionPrecisions.Exact) ?? same.FirstOrDefault();
            if (found is not null)
            {
                return new PlacementResult("found", found.Id);
            }
        }
        else
        {
            if (same.Find(c => string.Equals(c.Version, wanted, StringComparison.OrdinalIgnoreCase)) is { } exact)
            {
                return new PlacementResult("found", exact.Id);
            }

            // The coarse entry the person meant learns its version.
            if (same.Find(c => c.Origin == "manual" && c.VersionPrecision != VersionPrecisions.Exact) is { } coarse)
            {
                var values = await CheckAsync(coarse, wanted, cancellationToken);
                values.ApplyTo(coarse);
                coarse.ChangedAt = time.GetUtcNow();
                await db.SaveChangesAsync(cancellationToken);
                await ComponentKeys.RefreshAsync(db, catalog, [coarse], cancellationToken);
                await ChangedAsync("component.changed", coarse.Id, projectId, values, cancellationToken);
                return new PlacementResult("version_set", coarse.Id);
            }

            // A repository's component belongs to its repository (16.5): a hand-entered one beside it would duplicate the product.
            if (same.Find(c => c.Origin == "repository") is { } read)
            {
                return new PlacementResult("repository", read.Id);
            }
        }

        var now = time.GetUtcNow();
        var component = new Component { Id = Guid.CreateVersion7(), ProjectId = projectId, Kind = like.Kind, CreatedAt = now, ChangedAt = now };
        var added = await CheckAsync(like, wanted, cancellationToken, fresh: true);
        added.ApplyTo(component);
        db.Components.Add(component);
        await db.SaveChangesAsync(cancellationToken);
        await ComponentKeys.RefreshAsync(db, catalog, [component], cancellationToken);
        await ChangedAsync("component.added", component.Id, projectId, added, cancellationToken);
        return new PlacementResult("added", component.Id);
    }

    /// <summary>The component's product with the new version, through the checks of the endpoints; a new one takes the project's context.</summary>
    private async Task<ComponentValues> CheckAsync(Component source, string? version, CancellationToken cancellationToken, bool fresh = false)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var input = new ComponentInput(
            null, null, source.ProductKey, source.CustomVendor, source.CustomName, source.Kind, version ?? string.Empty, fresh ? "unknown" : source.Packaging, null, null,
            fresh ? null : source.Exposure, fresh ? null : source.Criticality, fresh ? null : source.Protection, fresh ? null : source.ProtectionNote,
            null, fresh ? null : source.NoticeThreshold);
        return await ComponentRules.CheckAsync(input, catalog, errors, string.Empty, cancellationToken)
            ?? throw new InvalidOperationException($"The placed component is not valid: {string.Join(" ", errors.SelectMany(e => e.Value))}");
    }

    private async Task ChangedAsync(string action, Guid component, Guid projectId, ComponentValues values, CancellationToken cancellationToken)
    {
        await audit.RecordAsync(new AuditEntry(action, "component", component.ToString(), values), cancellationToken);
        await jobs.EnqueueAsync(new ProjectProfileChanged(projectId), ProjectProfileChanged.Options(projectId), cancellationToken);
    }
}
