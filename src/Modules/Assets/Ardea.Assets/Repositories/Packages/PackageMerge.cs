using Ardea.Catalog.Contracts;

namespace Ardea.Assets.Repositories.Packages;

internal sealed class Findings
{
    private readonly Dictionary<(PackageCoordinate Package, string? Version), PackageFinding> _byKey = [];

    public void Add(PackageFinding finding)
    {
        var key = (finding.Package, finding.Version);
        _byKey[key] = _byKey.TryGetValue(key, out var existing) ? Combine(existing, finding) : finding;
    }

    public void AddRange(IEnumerable<PackageFinding> findings)
    {
        foreach (var finding in findings)
        {
            Add(finding);
        }
    }

    public List<PackageFinding> ToList() =>
        [.. _byKey.Values
            .OrderBy(f => f.Package.Type, StringComparer.Ordinal)
            .ThenBy(f => f.Package.Name, StringComparer.Ordinal)
            .ThenBy(f => f.Version, StringComparer.Ordinal)];

    private static PackageFinding Combine(PackageFinding a, PackageFinding b) => a with
    {
        Exact = a.Exact || b.Exact,
        Scope = a.Scope == DependencyScope.Runtime || b.Scope == DependencyScope.Runtime ? DependencyScope.Runtime
            : a.Scope == DependencyScope.Development || b.Scope == DependencyScope.Development ? DependencyScope.Development
            : DependencyScope.Unknown,
        Relation = a.Relation == DependencyRelation.Direct || b.Relation == DependencyRelation.Direct ? DependencyRelation.Direct
            : a.Relation == DependencyRelation.Transitive || b.Relation == DependencyRelation.Transitive ? DependencyRelation.Transitive
            : DependencyRelation.Unknown,
    };
}

internal static class PackageMerge
{
    public static IReadOnlyList<PackageFinding> Merge(IEnumerable<PackageFinding> sbom, IEnumerable<PackageFinding> lockfiles)
    {
        var merged = new Findings();
        var fromLockfiles = new HashSet<PackageCoordinate>();
        foreach (var finding in lockfiles)
        {
            merged.Add(finding);
            fromLockfiles.Add(finding.Package);
        }

        merged.AddRange(sbom.Where(f => !fromLockfiles.Contains(f.Package)));
        return merged.ToList();
    }
}
