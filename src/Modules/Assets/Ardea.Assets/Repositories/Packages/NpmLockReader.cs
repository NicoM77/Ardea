using System.Text.Json;
using Ardea.Catalog.Contracts;

namespace Ardea.Assets.Repositories.Packages;

/// <summary>
/// Reads npm's <c>package-lock.json</c> (master plan 16.3, ADR 032 decision 4): exact versions, the
/// <c>dev</c> flag and the path.
/// <list type="bullet">
/// <item>Version 2 and 3: the <c>packages</c> map keyed by install path. <c>dev: true</c> is development;
/// everything else, <c>devOptional</c> included (an optional dependency of a shipped package), is runtime.
/// A package directly under the root's or a workspace's <c>node_modules</c> whose name the root or a
/// workspace declares (<c>dependencies</c>, <c>devDependencies</c>, <c>optionalDependencies</c>,
/// <c>peerDependencies</c>) is direct; everything else, nested <c>node_modules</c> above all, is transitive.
/// Links and workspace folders are the project itself and are skipped.</item>
/// <item>Version 1: the nested <c>dependencies</c> map. It does not record what the root declares, so
/// top-level packages have an unknown relation and nested ones are transitive.</item>
/// </list>
/// The same package in the same version at several paths is one finding: runtime if any copy is, direct if any copy is.
/// </summary>
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
        // The project's own folders: the root ("") and its workspaces, i.e. every key outside node_modules.
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
            // An alias ("x": "npm:real@1") installs under its own folder but names the real package.
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
