using Ardea.Assets.Persistence;
using Ardea.Assets.Repositories.Packages;

namespace Ardea.Assets.Repositories.Indexing;

/// <summary>What a reading changes in the components of one link.</summary>
internal sealed class PackageChanges
{
    public List<(Component Component, PackageFinding Finding)> Created { get; } = [];

    public List<(Component Component, PackageFinding Finding)> Updated { get; } = [];

    public List<Component> Removed { get; } = [];

    public int Unchanged { get; set; }

    public bool Any => Created.Count + Updated.Count + Removed.Count > 0;
}

/// <summary>
/// The package components of a link follow the repository (master plan 16.5): first matched by
/// package and version, then whatever is left is paired per package, so a raised version changes the
/// same component instead of removing it and adding a new one. Exposure, criticality and protection,
/// which a person sets, are never touched. Components of the link without a package (accepted
/// platform suggestions) and manual components are outside this reconcile.
/// </summary>
internal static class PackageReconciler
{
    public static PackageChanges Reconcile(IReadOnlyList<Component> existing, IReadOnlyList<PackageFinding> findings)
    {
        var changes = new PackageChanges();
        var unmatched = existing.Where(c => c.Purl is not null).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToList();
        var open = new List<PackageFinding>();

        // Same package, same version: the component stays; scope, relation or evidence may change.
        foreach (var finding in findings)
        {
            var match = unmatched.FirstOrDefault(c => PackageOf(c) == Key(finding) && c.Version == finding.Version);
            if (match is null)
            {
                open.Add(finding);
                continue;
            }

            unmatched.Remove(match);
            if (Scope(finding) == match.DependencyScope && Relation(finding) == match.DependencyRelation)
            {
                changes.Unchanged++;
            }
            else
            {
                changes.Updated.Add((match, finding));
            }
        }

        // Same package, other version: the version changed. Pairs in version order, so 1.2 → 1.3 and 2.0 → 2.1.
        foreach (var group in open.GroupBy(Key).ToList())
        {
            var candidates = unmatched.Where(c => PackageOf(c) == group.Key).OrderBy(c => c.Version, StringComparer.Ordinal).ToList();
            foreach (var (finding, component) in group.OrderBy(f => f.Version, StringComparer.Ordinal).Zip(candidates))
            {
                unmatched.Remove(component);
                open.Remove(finding);
                changes.Updated.Add((component, finding));
            }
        }

        foreach (var finding in open)
        {
            changes.Created.Add((new Component { Id = Guid.CreateVersion7(), Kind = "library", Origin = "repository", Packaging = "upstream" }, finding));
        }

        changes.Removed.AddRange(unmatched);
        return changes;
    }

    public static string Key(PackageFinding finding) => finding.Package.ToString();

    public static string? Scope(PackageFinding f) => f.Scope switch
    {
        DependencyScope.Runtime => "runtime",
        DependencyScope.Development => "development",
        _ => "unknown",
    };

    public static string? Relation(PackageFinding f) => f.Relation switch
    {
        DependencyRelation.Direct => "direct",
        DependencyRelation.Transitive => "transitive",
        _ => "unknown",
    };

    /// <summary><c>pkg:type/name</c> of a stored purl, without version.</summary>
    private static string? PackageOf(Component c) =>
        Catalog.Contracts.PackageCoordinate.TryParsePurl(c.Purl, out var coordinate, out _) ? coordinate.ToString() : null;
}
