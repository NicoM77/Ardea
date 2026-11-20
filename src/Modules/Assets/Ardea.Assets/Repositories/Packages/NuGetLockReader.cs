using System.Text.Json;
using System.Text.RegularExpressions;
using Ardea.Catalog.Contracts;

namespace Ardea.Assets.Repositories.Packages;

internal static partial class NuGetLockReader
{
    public static PackageReadResult Read(ReadOnlyMemory<byte> utf8, string path)
    {
        if (utf8.Length > PackageReaders.MaxLockfileBytes)
        {
            return PackageReadResult.Failed($"{path} is larger than {PackageReaders.MaxLockfileBytes} bytes.");
        }

        try
        {
            using var document = JsonDocument.Parse(utf8, PackageReaders.Document);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("dependencies", out var frameworks) || frameworks.ValueKind != JsonValueKind.Object)
            {
                return PackageReadResult.Failed($"{path} is not a packages.lock.json.");
            }

            var lines = PackageReaders.ObjectKeyLines(utf8.Span, _ => true);
            var scope = IsTestProject(path) ? DependencyScope.Development : DependencyScope.Runtime;
            var findings = new Findings();
            foreach (var framework in frameworks.EnumerateObject().Where(f => f.Value.ValueKind == JsonValueKind.Object))
            {
                foreach (var package in framework.Value.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object))
                {
                    var type = String(package.Value, "type");
                    var relation = type switch
                    {
                        "Direct" => DependencyRelation.Direct,
                        "Transitive" or "CentralTransitive" => DependencyRelation.Transitive,
                        _ => DependencyRelation.Unknown,
                    };
                    if (type == "Project")
                    {
                        continue;
                    }

                    var version = String(package.Value, "resolved");
                    var plain = PackageReaders.IsPlainVersion(version);
                    findings.Add(new PackageFinding(
                        PackageCoordinate.Normalize("nuget", package.Name),
                        plain ? version : null,
                        plain,
                        scope,
                        relation,
                        new FindingEvidence(FindingSource.Lockfile, path, lines.TryGetValue(package.Name, out var line) ? line : null)));
                }
            }

            return new PackageReadResult(findings.ToList(), null);
        }
        catch (JsonException ex)
        {
            return PackageReadResult.Failed($"{path} is not valid JSON (line {ex.LineNumber + 1}).");
        }
    }

    public static bool IsTestProject(string path)
    {
        var folders = path.Split('/');
        return folders[..^1].Any(f => f.Equals("test", StringComparison.OrdinalIgnoreCase) || f.Equals("tests", StringComparison.OrdinalIgnoreCase) || TestSuffix().IsMatch(f));
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [GeneratedRegex(@"\.(?:[A-Z][A-Za-z]*)?Tests?$")]
    private static partial Regex TestSuffix();
}
