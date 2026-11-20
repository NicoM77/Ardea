using System.Text.Json;
using Ardea.Catalog.Contracts;

namespace Ardea.Assets.Repositories.Packages;

internal static class NpmLockReader
{
    private const string NodeModules = "node_modules/";

    private static readonly string[] DeclarationKeys = ["dependencies", "devDependencies", "optionalDependencies", "peerDependencies"];

    public static PackageReadResult Read(ReadOnlyMemory<byte> utf8, string path)
    {
        if (utf8.Length > PackageReaders.MaxLockfileBytes)
        {
            return PackageReadResult.Failed($"{path} is larger than {PackageReaders.MaxLockfileBytes} bytes.");
        }

        try
        {
            using var document = JsonDocument.Parse(utf8, PackageReaders.Document);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return PackageReadResult.Failed($"{path} is not a package-lock.json.");
            }

            if (root.TryGetProperty("packages", out var packages) && packages.ValueKind == JsonValueKind.Object)
            {
                var lines = PackageReaders.ObjectKeyLines(utf8.Span, key => key.Contains(NodeModules, StringComparison.Ordinal));
                return new PackageReadResult(ReadPackages(packages, path, lines), null);
            }

            if (root.TryGetProperty("dependencies", out var dependencies) && dependencies.ValueKind == JsonValueKind.Object)
            {
                var findings = new Findings();
                ReadVersion1(dependencies, path, nested: false, findings, depth: 0);
                return new PackageReadResult(findings.ToList(), null);
            }

            return PackageReadResult.Failed($"{path} has neither \"packages\" nor \"dependencies\".");
        }
        catch (JsonException ex)
        {
            return PackageReadResult.Failed($"{path} is not valid JSON (line {ex.LineNumber + 1}).");
        }
    }

    private static List<PackageFinding> ReadPackages(JsonElement packages, string path, Dictionary<string, int> lines)
    {
        var projectFolders = new HashSet<string>(StringComparer.Ordinal);
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in packages.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object || entry.Name.StartsWith(NodeModules, StringComparison.Ordinal) || entry.Name.Contains("/" + NodeModules, StringComparison.Ordinal))
            {
                continue;
            }

            projectFolders.Add(entry.Name);
            foreach (var key in DeclarationKeys)
            {
                if (entry.Value.TryGetProperty(key, out var names) && names.ValueKind == JsonValueKind.Object)
                {
                    declared.UnionWith(names.EnumerateObject().Select(n => n.Name));
                }
            }
        }

        var findings = new Findings();
        foreach (var entry in packages.EnumerateObject())
        {
            var cut = entry.Name.LastIndexOf(NodeModules, StringComparison.Ordinal);
            if (cut < 0 || entry.Value.ValueKind != JsonValueKind.Object || (cut > 0 && entry.Name[cut - 1] != '/'))
            {
                continue;
            }

            var value = entry.Value;
            if (Bool(value, "link") || String(value, "resolved") is { } resolved && resolved.StartsWith("file:", StringComparison.Ordinal))
            {
                continue;
            }

            var folderName = entry.Name[(cut + NodeModules.Length)..];
            var name = String(value, "name") ?? folderName;
            var parent = cut == 0 ? string.Empty : entry.Name[..(cut - 1)];
            var relation = projectFolders.Contains(parent) && declared.Contains(folderName) ? DependencyRelation.Direct : DependencyRelation.Transitive;
            var version = String(value, "version");
            var plain = PackageReaders.IsPlainVersion(version);
            findings.Add(new PackageFinding(
                PackageCoordinate.Normalize("npm", name),
                plain ? version : null,
                plain,
                Bool(value, "dev") ? DependencyScope.Development : DependencyScope.Runtime,
                relation,
                new FindingEvidence(FindingSource.Lockfile, path, lines.TryGetValue(entry.Name, out var line) ? line : null)));
        }

        return findings.ToList();
    }

    private static void ReadVersion1(JsonElement dependencies, string path, bool nested, Findings findings, int depth)
    {
        if (depth > 100)
        {
            return;
        }

        foreach (var entry in dependencies.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var version = String(entry.Value, "version");
            if (version is not null && version.StartsWith("file:", StringComparison.Ordinal))
            {
                continue;
            }

            var plain = PackageReaders.IsPlainVersion(version);
            findings.Add(new PackageFinding(
                PackageCoordinate.Normalize("npm", entry.Name),
                plain ? version : null,
                plain,
                Bool(entry.Value, "dev") ? DependencyScope.Development : DependencyScope.Runtime,
                nested ? DependencyRelation.Transitive : DependencyRelation.Unknown,
                new FindingEvidence(FindingSource.Lockfile, path)));
            if (entry.Value.TryGetProperty("dependencies", out var children) && children.ValueKind == JsonValueKind.Object)
            {
                ReadVersion1(children, path, nested: true, findings, depth + 1);
            }
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
