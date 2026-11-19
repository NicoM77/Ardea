using Ardea.Assets.Contracts;
using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assets.Features;

/// <summary>Profiles for the Assessment module (master plan 3.2, 7.2, 9.2), in the caller's unit of work.</summary>
internal sealed class ProfileQueries(AssetsDbContext db, IProductCatalog catalog) : IProfileQueries
{
    public async Task<IReadOnlyList<Guid>> OrganizationsWithKeysAsync(IReadOnlyCollection<ProfileKey> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        // The system function reads across organisations and returns ids only (ADR 029, master plan 9.2).
        var types = keys.Select(k => k.Type).ToArray();
        var values = keys.Select(k => k.Value).ToArray();
        return await db.Database.SqlQuery<Guid>($"""
            SELECT organization_id AS "Value" FROM assets.organizations_with_component_keys({types}::text[], {values}::text[]) ORDER BY 1
            """).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProfileKeyHit>> FindByKeysAsync(IReadOnlyCollection<ProfileKey> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        // One lookup per key type on ix_component_key_value; the tenant filter narrows to the organisation.
        var hits = new List<ProfileKeyHit>();
        foreach (var group in keys.GroupBy(k => k.Type, StringComparer.Ordinal))
        {
            var type = group.Key;
            var values = group.Select(k => k.Value).Distinct(StringComparer.Ordinal).ToArray();
            var found = await (
                from key in db.ComponentKeys.AsNoTracking()
                join project in db.Projects.AsNoTracking() on key.ProjectId equals project.Id
                where key.KeyType == type && values.Contains(key.KeyValue) && project.ArchivedAt == null
                select new ProfileKeyHit(key.ProjectId, key.ComponentId, key.KeyType, key.KeyValue, key.Strength))
                .ToListAsync(cancellationToken);
            hits.AddRange(found);
        }

        return hits;
    }

    public async Task<ProjectProfile?> FindProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var components = await db.Components.AsNoTracking().Where(c => c.ProjectId == projectId).OrderBy(c => c.Id).ToListAsync(cancellationToken);
        return new ProjectProfile(
            project.Id, project.Name, project.Criticality, project.Exposure, project.ArchivedAt is not null, project.CreatedAt,
            await ProfilesAsync(components, new Dictionary<Guid, Project> { [project.Id] = project }, cancellationToken));
    }

    public async Task<IReadOnlyList<ProfileComponent>> FindComponentsAsync(IReadOnlyCollection<Guid> componentIds, CancellationToken cancellationToken)
    {
        if (componentIds.Count == 0)
        {
            return [];
        }

        var ids = componentIds.Distinct().ToArray();
        var components = await db.Components.AsNoTracking().Where(c => ids.Contains(c.Id)).OrderBy(c => c.Id).ToListAsync(cancellationToken);
        var projectIds = components.Select(c => c.ProjectId).Distinct().ToArray();
        var projects = await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        return await ProfilesAsync(components, projects, cancellationToken);
    }

    private async Task<IReadOnlyList<ProfileComponent>> ProfilesAsync(List<Component> components, IReadOnlyDictionary<Guid, Project> projects, CancellationToken cancellationToken)
    {
        var ids = components.Select(c => c.Id).ToArray();
        var keys = (await db.ComponentKeys.AsNoTracking().Where(k => ids.Contains(k.ComponentId)).ToListAsync(cancellationToken))
            .ToLookup(k => k.ComponentId);
        var result = new List<ProfileComponent>(components.Count);
        foreach (var c in components)
        {
            var project = projects[c.ProjectId];
            var product = c.ProductKey is null ? null : await catalog.FindByKeyAsync(c.ProductKey, cancellationToken);
            var own = keys[c.Id].OrderBy(k => k.KeyType, StringComparer.Ordinal).ThenBy(k => k.KeyValue, StringComparer.Ordinal).ToList();
            result.Add(new ProfileComponent
            {
                Id = c.Id,
                ProjectId = c.ProjectId,
                ProjectArchived = project.ArchivedAt is not null,
                ProductKey = c.ProductKey,
                CustomVendor = c.CustomVendor,
                CustomName = c.CustomName,
                Name = product?.Name ?? c.CustomName ?? c.ProductKey ?? c.Purl ?? "?",
                Kind = c.Kind,
                Version = c.Version,
                VersionScheme = c.VersionScheme ?? product?.VersionScheme,
                Packaging = c.Packaging,
                Purl = c.Purl,
                Exposure = c.Exposure ?? project.Exposure,
                Criticality = c.Criticality ?? project.Criticality,
                Protection = c.Protection,
                ProtectionNote = c.ProtectionNote,
                Origin = c.Origin,
                RepositoryLinkId = c.RepositoryLinkId,
                DependencyScope = c.DependencyScope,
                StrongKeys = [.. own.Where(k => k.Strength == "strong").Select(k => new ProfileKey(k.KeyType, k.KeyValue))],
                WeakKeys = [.. own.Where(k => k.Strength == "weak").Select(k => new ProfileKey(k.KeyType, k.KeyValue))],
            });
        }

        return result;
    }
}
