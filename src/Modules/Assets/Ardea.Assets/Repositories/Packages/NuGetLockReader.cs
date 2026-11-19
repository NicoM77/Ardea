using System.Text.Json;
using System.Text.RegularExpressions;
using Ardea.Catalog.Contracts;

namespace Ardea.Assets.Repositories.Packages;

/// <summary>
/// Reads NuGet's <c>packages.lock.json</c> (master plan 16.3, ADR 032 decision 4), which GitHub's
/// dependency graph does not resolve: per target framework (and runtime), the resolved version of
/// every package. <c>Direct</c> is direct; <c>Transitive</c> and <c>CentralTransitive</c> (a transitive
/// package pinned by central package management) are transitive; <c>Project</c> references are the
/// repository's own code and skipped. The same package and version under several target frameworks is
/// one finding, direct if it is direct in any.
/// <para>
/// The lockfile does not say whether its project is shipped. Heuristic: a lockfile under a folder named
/// <c>test</c> or <c>tests</c>, or in a folder whose name ends in <c>.Tests</c>, <c>.Test</c> or a
/// similar suffix (<c>.UnitTests</c>, <c>.IntegrationTest</c>), belongs to a test project: development.
/// Everything else is runtime.
/// </para>
/// </summary>
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

    /// <summary>The heuristic above, on the folders of <paramref name="path"/> (the file name itself does not count).</summary>
    public static bool IsTestProject(string path)
    {
        var folders = path.Split('/');
        return folders[..^1].Any(f => f.Equals("test", StringComparison.OrdinalIgnoreCase) || f.Equals("tests", StringComparison.OrdinalIgnoreCase) || TestSuffix().IsMatch(f));
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary><c>.Tests</c>, <c>.Test</c>, <c>.UnitTests</c>, <c>.IntegrationTest</c>; capital T, so <c>.Contest</c> is no test.</summary>
    [GeneratedRegex(@"\.(?:[A-Z][A-Za-z]*)?Tests?$")]
    private static partial Regex TestSuffix();
}
