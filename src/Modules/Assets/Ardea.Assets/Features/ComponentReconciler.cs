using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;

namespace Ardea.Assets.Features;

internal sealed class ReconcileResult
{
    public List<Component> Created { get; } = [];

    public List<Component> Updated { get; } = [];

    public List<Component> Removed { get; } = [];

    public int Unchanged { get; set; }

    public Dictionary<string, string[]> Errors { get; } = new(StringComparer.Ordinal);
}

internal static class ComponentReconciler
{
    public static async Task<ReconcileResult> ReconcileAsync(
        Guid projectId, IReadOnlyList<ComponentInput> items, List<Component> existing, IProductCatalog catalog, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var result = new ReconcileResult();
        var byId = existing.ToDictionary(c => c.Id);
        var values = new ComponentValues?[items.Count];
        var refs = new Dictionary<string, int>(StringComparer.Ordinal);
        var ids = new HashSet<Guid>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var prefix = $"components[{i}].";
            values[i] = await ComponentRules.CheckAsync(item, catalog, result.Errors, prefix, cancellationToken);
            if (item.Ref is { Length: > 0 } reference && !refs.TryAdd(reference, i))
            {
                result.Errors.TryAdd(prefix + "ref", ["Each ref may appear only once."]);
            }

            if (item.Id is { } id)
            {
                if (!byId.TryGetValue(id, out var component))
                {
                    result.Errors.TryAdd(prefix + "id", ["No such component in this project."]);
                }
                else if (component.Origin != "manual")
                {
                    result.Errors.TryAdd(prefix + "id", ["Components from a repository are not replaced; detach the component first."]);
                }
                else if (!ids.Add(id))
                {
                    result.Errors.TryAdd(prefix + "id", ["Each component may appear only once."]);
                }
            }

            if (item.ParentId is { } parentId && !byId.ContainsKey(parentId))
            {
                result.Errors.TryAdd(prefix + "parentId", ["No such component in this project."]);
            }
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].ParentRef is { Length: > 0 } parentRef && (!refs.TryGetValue(parentRef, out var parent) || parent == i))
            {
                result.Errors.TryAdd($"components[{i}].parentRef", ["No other item has this ref."]);
            }
        }

        if (result.Errors.Count > 0)
        {
            return result;
        }

        var unmatched = existing.Where(c => c.Origin == "manual").OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToList();
        var assigned = new Guid?[items.Count];
        var pending = Enumerable.Range(0, items.Count).ToList();
        while (pending.Count > 0)
        {
            var ready = pending.Where(i => items[i].ParentRef is not { Length: > 0 } parentRef || assigned[refs[parentRef]] is not null).ToList();
            if (ready.Count == 0)
            {
                foreach (var i in pending)
                {
                    result.Errors.TryAdd($"components[{i}].parentRef", ["The parents form a cycle."]);
                }

                return result;
            }

            foreach (var i in ready)
            {
                var item = items[i];
                var value = values[i]!;
                var parent = item.ParentRef is { Length: > 0 } parentRef ? assigned[refs[parentRef]] : item.ParentId;
                var target = item.Id is { } id
                    ? unmatched.Single(c => c.Id == id)
                    : unmatched.FirstOrDefault(c => !ids.Contains(c.Id) && ComponentValues.IdentityOf(c) == value.Identity && c.ParentComponentId == parent);
                if (target is null)
                {
                    target = new Component { Id = Guid.CreateVersion7(), ProjectId = projectId, Kind = value.Kind, ParentComponentId = parent, CreatedAt = now, ChangedAt = now };
                    value.ApplyTo(target);
                    result.Created.Add(target);
                }
                else
                {
                    unmatched.Remove(target);
                    if (value.SameAs(target) && target.ParentComponentId == parent)
                    {
                        result.Unchanged++;
                    }
                    else
                    {
                        value.ApplyTo(target);
                        target.ParentComponentId = parent;
                        target.ChangedAt = now;
                        result.Updated.Add(target);
                    }
                }

                assigned[i] = target.Id;
                pending.Remove(i);
            }
        }

        result.Removed.AddRange(unmatched);
        var removed = result.Removed.Select(c => c.Id).ToHashSet();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].ParentId is { } parentId && removed.Contains(parentId))
            {
                result.Errors.TryAdd($"components[{i}].parentId", ["The parent is not in the new list."]);
            }
        }

        return result;
    }
}
