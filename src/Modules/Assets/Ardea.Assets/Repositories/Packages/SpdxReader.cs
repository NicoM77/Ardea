using System.Text.Json;
using Ardea.Catalog.Contracts;

namespace Ardea.Assets.Repositories.Packages;

internal sealed record SbomReadResult(IReadOnlyList<PackageFinding> Findings, string? DescribedBranch, string? Error)
{
    public bool Ok => Error is null;

    public static SbomReadResult Failed(string error) => new([], null, error);
}

internal static class SpdxReader
{
    public const long MaxSbomBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32,
    };

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
