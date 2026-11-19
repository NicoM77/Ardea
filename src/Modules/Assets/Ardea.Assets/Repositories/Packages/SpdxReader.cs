using System.Text.Json;
using Ardea.Catalog.Contracts;

namespace Ardea.Assets.Repositories.Packages;

/// <summary>
/// What the SBOM gave: the packages, and the branch its root package names (GitHub exports the default
/// branch only, e.g. <c>master</c>), or why it gave nothing.
/// </summary>
internal sealed record SbomReadResult(IReadOnlyList<PackageFinding> Findings, string? DescribedBranch, string? Error)
{
    public bool Ok => Error is null;

    public static SbomReadResult Failed(string error) => new([], null, error);
}

/// <summary>
/// Reads the SPDX 2.3 JSON of GitHub's dependency graph (ADR 032), streaming, without a document tree.
/// <list type="bullet">
/// <item>Every package with a purl becomes a finding. Its version comes from the purl, else from
/// <c>versionInfo</c>; a range (<c>^2.0.0</c>, <c>~3.0.2</c>, <c>&gt;= 0</c>, <c>1.x</c>, <c>*</c>) is
/// "version unknown". NuGet versions from the graph are never exact: GitHub reads only the project
/// files, where <c>1.0.0</c> means "at least 1.0.0".</item>
/// <item>The root package (what the document <c>DESCRIBES</c>) is the repository itself, and
/// <c>pkg:github/…</c> packages are skipped. GitHub Actions (<c>pkg:githubactions/…</c>) are kept: the
/// coverage lists them as not yet checkable (16.3).</item>
/// <item>Scope is always unknown: the SBOM does not mark development dependencies.</item>
/// <item>Relation: only where the SBOM carries real paths for an ecosystem, i.e. some package of that
/// purl type itself <c>DEPENDS_ON</c> another (GitHub has a lockfile), is a package the root depends on
/// direct and every other transitive. Without paths every package hangs off the root, which says
/// nothing; the relation stays unknown.</item>
/// </list>
/// </summary>
internal static class SpdxReader
{
    /// <summary>ADR 032, decision 2.</summary>
    public const long MaxSbomBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32,
    };

    /// <summary>Reads the SBOM from <paramref name="utf8"/>. Fails as a result for malformed input; a stream beyond <see cref="MaxSbomBytes"/> throws <see cref="ContentTooLargeException"/>.</summary>
    public static async Task<SbomReadResult> ReadAsync(Stream utf8, CancellationToken cancellationToken)
    {
        SpdxDocument? document;
        try
        {
            await using var bounded = new BoundedStream(utf8, MaxSbomBytes);
            document = await JsonSerializer.DeserializeAsync<SpdxDocument>(bounded, Json, cancellationToken);
        }
        catch (JsonException ex)
        {
            return SbomReadResult.Failed($"The SBOM is not valid JSON (line {ex.LineNumber + 1}).");
        }

        if (document?.SpdxVersion is not { } version || !version.StartsWith("SPDX-2.", StringComparison.Ordinal))
        {
            return SbomReadResult.Failed("The SBOM is no SPDX 2 document.");
        }

        return Read(document);
    }

    private static SbomReadResult Read(SpdxDocument document)
    {
        var packages = (document.Packages ?? []).Where(p => p?.SpdxId is not null).GroupBy(p => p.SpdxId!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var relationships = (document.Relationships ?? []).Where(r => r?.SpdxElementId is not null && r.RelatedSpdxElement is not null).ToList();

        var roots = new HashSet<string>(document.DocumentDescribes ?? [], StringComparer.Ordinal);
        roots.UnionWith(relationships.Where(r => r.RelationshipType == "DESCRIBES" && r.SpdxElementId == document.SpdxId).Select(r => r.RelatedSpdxElement!));

        var coordinates = new Dictionary<string, (PackageCoordinate Package, string? Version)>(StringComparer.Ordinal);
        foreach (var (id, package) in packages)
        {
            if (Purl(package) is { } purl && PackageCoordinate.TryParsePurl(purl, out var coordinate, out var purlVersion))
            {
                coordinates[id] = (coordinate, purlVersion);
            }
        }

        var dependsOn = relationships.Where(r => r.RelationshipType == "DEPENDS_ON").ToList();
        var withPaths = dependsOn
            .Where(r => !roots.Contains(r.SpdxElementId!) && coordinates.ContainsKey(r.SpdxElementId!))
            .Select(r => coordinates[r.SpdxElementId!].Package.Type)
            .ToHashSet(StringComparer.Ordinal);
        var direct = dependsOn.Where(r => roots.Contains(r.SpdxElementId!)).Select(r => r.RelatedSpdxElement!).ToHashSet(StringComparer.Ordinal);

        var findings = new Findings();
        foreach (var (id, (coordinate, purlVersion)) in coordinates)
        {
            if (roots.Contains(id) || coordinate.Type == "github")
            {
                continue;
            }

            var version = PackageReaders.IsPlainVersion(purlVersion) ? purlVersion
                : purlVersion is null && PackageReaders.IsPlainVersion(packages[id].VersionInfo) ? packages[id].VersionInfo
                : null;
            var relation = !withPaths.Contains(coordinate.Type) ? DependencyRelation.Unknown
                : direct.Contains(id) ? DependencyRelation.Direct
                : DependencyRelation.Transitive;
            findings.Add(new PackageFinding(
                coordinate,
                version,
                version is not null && coordinate.Type != "nuget",
                DependencyScope.Unknown,
                relation,
                new FindingEvidence(FindingSource.Sbom, null)));
        }

        var branch = roots.Select(r => packages.GetValueOrDefault(r)?.VersionInfo).FirstOrDefault(v => v is not null);
        return new SbomReadResult(findings.ToList(), branch, null);
    }

    private static string? Purl(SpdxPackage package) =>
        package.ExternalRefs?.FirstOrDefault(r => r?.ReferenceType == "purl" && r.ReferenceCategory is "PACKAGE-MANAGER" or "PACKAGE_MANAGER")?.ReferenceLocator;

    private sealed class SpdxDocument
    {
        public string? SpdxVersion { get; init; }

        public string? SpdxId { get; init; }

        public List<string>? DocumentDescribes { get; init; }

        public List<SpdxPackage>? Packages { get; init; }

        public List<SpdxRelationship>? Relationships { get; init; }
    }

    private sealed class SpdxPackage
    {
        public string? SpdxId { get; init; }

        public string? Name { get; init; }

        public string? VersionInfo { get; init; }

        public List<SpdxExternalRef>? ExternalRefs { get; init; }
    }

    private sealed class SpdxExternalRef
    {
        public string? ReferenceCategory { get; init; }

        public string? ReferenceType { get; init; }

        public string? ReferenceLocator { get; init; }
    }

    private sealed class SpdxRelationship
    {
        public string? SpdxElementId { get; init; }

        public string? RelatedSpdxElement { get; init; }

        public string? RelationshipType { get; init; }
    }
}
