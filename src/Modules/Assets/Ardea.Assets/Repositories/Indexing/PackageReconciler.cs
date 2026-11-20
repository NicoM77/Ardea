using Ardea.Assets.Persistence;
using Ardea.Assets.Repositories.Packages;

namespace Ardea.Assets.Repositories.Indexing;

internal sealed class PackageChanges
{
    public List<(Component Component, PackageFinding Finding)> Created { get; } = [];

    public List<(Component Component, PackageFinding Finding)> Updated { get; } = [];

    public List<Component> Removed { get; } = [];

    public int Unchanged { get; set; }

    public bool Any => Created.Count + Updated.Count + Removed.Count > 0;
}

internal static class PackageReconciler
{
    public static PackageChanges Reconcile(IReadOnlyList<Component> existing, IReadOnlyList<PackageFinding> findings)
    {
        var changes = new PackageChanges();
        var unmatched = existing.Where(c => c.Purl is not null).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToList();
        var open = new List<PackageFinding>();

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

    private static string? PackageOf(Component c) =>
        Catalog.Contracts.PackageCoordinate.TryParsePurl(c.Purl, out var coordinate, out _) ? coordinate.ToString() : null;
}
