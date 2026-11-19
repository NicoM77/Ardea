using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Ardea.Catalog.Contracts;

namespace Ardea.Assets.Repositories.Detection;

/// <summary>A file of the repository at the read commit; paths are compared, never resolved (master plan 16.9).</summary>
internal sealed record FileContent(string Path, string Content);

/// <summary>Where a suggestion comes from: file, line (1-based) and the value read, e.g. <c>FROM mcr.microsoft.com/dotnet/aspnet:10.0</c>.</summary>
internal sealed record Evidence(string File, int Line, string Value);

/// <summary>
/// A platform the repository suggests (master plan 16.3): a catalogue product with a version line
/// such as <c>10.0</c> or <c>18</c>, never an exact version; the user confirms, changes or dismisses it.
/// </summary>
internal sealed record PlatformSuggestion(string ProductKey, string? VersionLine, IReadOnlyList<Evidence> Evidence);

/// <summary>
/// Detection rules for platform files (master plan 16.3; task 2.12): <c>FROM</c> in Dockerfiles,
/// <c>image</c> in Compose files, <c>global.json</c>, <c>TargetFramework</c> in project files,
/// <c>engines</c> and <c>.nvmrc</c>, <c>require.php</c> in <c>composer.json</c> and the <c>go</c>
/// directive in <c>go.mod</c>. Images and products go through the catalogue; what it does not know
/// is not suggested. The rules only read text: nothing is executed, no entity is resolved.
/// </summary>
internal static partial class PlatformDetection
{
    private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 64, CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Whether a path of the tree is a file the rules read; the job fetches only these.</summary>
    public static bool IsCandidate(string path)
    {
        var name = FileName(path);
        return name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".dockerfile", StringComparison.OrdinalIgnoreCase)
            || ComposeName().IsMatch(name)
            || name.Equals("global.json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
            || name.Equals("package.json", StringComparison.Ordinal)
            || name.Equals(".nvmrc", StringComparison.Ordinal)
            || name.Equals(".node-version", StringComparison.Ordinal)
            || name.Equals("composer.json", StringComparison.Ordinal)
            || name.Equals("go.mod", StringComparison.Ordinal);
    }

    /// <summary>Applies every rule; one suggestion per product and version line, with all its evidence.</summary>
    public static async Task<IReadOnlyList<PlatformSuggestion>> DetectAsync(IEnumerable<FileContent> files, IProductCatalog catalog, CancellationToken cancellationToken)
    {
        var found = new List<(string Key, string? Line, Evidence Evidence)>();
        foreach (var file in files)
        {
            var name = FileName(file.Path);
            IEnumerable<(string Target, bool IsImage, string? Line, Evidence Evidence)> hits =
                name.Contains("ockerfile", StringComparison.OrdinalIgnoreCase) ? Dockerfile(file)
                : ComposeName().IsMatch(name) ? Compose(file)
                : name.Equals("global.json", StringComparison.OrdinalIgnoreCase) ? GlobalJson(file)
                : name.EndsWith("proj", StringComparison.OrdinalIgnoreCase) ? ProjectFile(file)
                : name == "package.json" ? PackageJson(file)
                : name is ".nvmrc" or ".node-version" ? NodeVersionFile(file)
                : name == "composer.json" ? ComposerJson(file)
                : name == "go.mod" ? GoMod(file)
                : [];
            foreach (var (target, isImage, line, evidence) in hits)
            {
                var product = isImage ? await catalog.FindByImageAsync(target, cancellationToken) : await catalog.FindByKeyAsync(target, cancellationToken);
                if (product is not null)
                {
                    found.Add((product.Key, line, evidence));
                }
            }
        }

        return [.. found.GroupBy(f => (f.Key, f.Line))
            .Select(g => new PlatformSuggestion(g.Key.Key, g.Key.Line, [.. g.Select(f => f.Evidence).Distinct()]))
            .OrderBy(s => s.ProductKey, StringComparer.Ordinal).ThenBy(s => s.VersionLine, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The version line of an image tag or a version: the leading numbers, at most major.minor
    /// (<c>10.0-noble</c> → <c>10.0</c>, <c>18-alpine</c> → <c>18</c>, <c>8.4.3-fpm</c> → <c>8.4</c>,
    /// <c>v20.11.1</c> → <c>20.11</c>); <c>null</c> for <c>latest</c> and names.
    /// </summary>
    public static string? VersionLine(string? text)
    {
        var match = text is null ? null : LeadingVersion().Match(text.Trim());
        return match is { Success: true } ? match.Groups["line"].Value : null;
    }

    private static IEnumerable<(string, bool, string?, Evidence)> Dockerfile(FileContent file)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal);
        var stages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = Lines(file.Content);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (ArgLine().Match(line) is { Success: true } arg && arg.Groups["value"].Success)
            {
                args[arg.Groups["name"].Value] = arg.Groups["value"].Value.Trim('"', '\'');
                continue;
            }

            if (FromLine().Match(line) is not { Success: true } from)
            {
                continue;
            }

            if (from.Groups["stage"].Success)
            {
                stages.Add(from.Groups["stage"].Value);
            }

            // ${VERSION} from an ARG with a default; unresolved variables give no suggestion.
            var reference = VariableReference().Replace(from.Groups["image"].Value, m => args.GetValueOrDefault(m.Groups["name"].Value) ?? m.Value);
            if (reference.Contains('$', StringComparison.Ordinal) || reference.Equals("scratch", StringComparison.OrdinalIgnoreCase) || stages.Contains(reference))
            {
                continue;
            }

            var (image, tag) = SplitImage(reference);
            yield return (image, true, VersionLine(tag), new Evidence(file.Path, i + 1, line));
        }
    }

    private static IEnumerable<(string, bool, string?, Evidence)> Compose(FileContent file)
    {
        var lines = Lines(file.Content);
        for (var i = 0; i < lines.Length; i++)
        {
            if (ComposeImage().Match(lines[i]) is { Success: true } match && !match.Groups["image"].Value.Contains('$', StringComparison.Ordinal))
            {
                var (image, tag) = SplitImage(match.Groups["image"].Value);
                yield return (image, true, VersionLine(tag), new Evidence(file.Path, i + 1, lines[i].Trim()));
            }
        }
    }

    private static IEnumerable<(string, bool, string?, Evidence)> GlobalJson(FileContent file)
    {
        var version = ReadJson(file, root => root.TryGetProperty("sdk", out var sdk) && sdk.TryGetProperty("version", out var v) ? v.GetString() : null);
        return version is null ? [] : [("dotnet", false, VersionLine(version), new Evidence(file.Path, LineOf(file.Content, version), $"sdk.version {version}"))];
    }

    private static IEnumerable<(string, bool, string?, Evidence)> ProjectFile(FileContent file)
    {
        var results = new List<(string, bool, string?, Evidence)>();
        string? sdk = null;
        var frameworks = new List<string>();
        try
        {
            // No DTD, no external entities: a project file is data here (master plan 16.9).
            using var reader = XmlReader.Create(new StringReader(file.Content), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 1_000_000,
                IgnoreComments = true,
            });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.LocalName == "Project" && reader.GetAttribute("Sdk") is { } projectSdk)
                {
                    sdk = projectSdk;
                }
                else if (reader.LocalName is "TargetFramework" or "TargetFrameworks")
                {
                    frameworks.AddRange(reader.ReadElementContentAsString().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
            }
        }
        catch (XmlException)
        {
            return [];
        }

        foreach (var framework in frameworks.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var evidence = new Evidence(file.Path, LineOf(file.Content, framework), $"TargetFramework {framework}");
            if (ModernFramework().Match(framework) is { Success: true } modern)
            {
                var line = modern.Groups["line"].Value;
                results.Add(("dotnet", false, line, evidence));
                if (sdk?.Equals("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) == true)
                {
                    results.Add(("aspnetcore", false, line, evidence with { Value = $"Sdk {sdk}, TargetFramework {framework}" }));
                }
            }
            else if (LegacyFramework().Match(framework) is { Success: true } legacy)
            {
                var digits = legacy.Groups["digits"].Value;
                results.Add(("dotnet-framework", false, digits.Length == 1 ? digits : $"{digits[0]}.{digits[1..]}", evidence));
            }
        }

        return results;
    }

    private static IEnumerable<(string, bool, string?, Evidence)> PackageJson(FileContent file)
    {
        var node = ReadJson(file, root => root.TryGetProperty("engines", out var engines) && engines.ValueKind == JsonValueKind.Object
            && engines.TryGetProperty("node", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null);
        // engines is a range (">=20", "^22.11"); its lowest major is the line the project builds for.
        return node is null || VersionLine(RangeStart().Match(node).Value) is not { } line
            ? []
            : [("nodejs", false, line.Split('.')[0], new Evidence(file.Path, LineOf(file.Content, node), $"engines.node {node}"))];
    }

    private static IEnumerable<(string, bool, string?, Evidence)> NodeVersionFile(FileContent file)
    {
        var text = Lines(file.Content).FirstOrDefault(l => l.Trim().Length > 0)?.Trim();
        // lts/iron and similar aliases name no version.
        return VersionLine(text) is { } line ? [("nodejs", false, line.Split('.')[0], new Evidence(file.Path, 1, text!))] : [];
    }

    private static IEnumerable<(string, bool, string?, Evidence)> ComposerJson(FileContent file)
    {
        var php = ReadJson(file, root => root.TryGetProperty("require", out var require) && require.ValueKind == JsonValueKind.Object
            && require.TryGetProperty("php", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null);
        return php is null || VersionLine(RangeStart().Match(php).Value) is not { } line
            ? []
            : [("php", false, line, new Evidence(file.Path, LineOf(file.Content, php), $"require.php {php}"))];
    }

    private static IEnumerable<(string, bool, string?, Evidence)> GoMod(FileContent file)
    {
        var lines = Lines(file.Content);
        for (var i = 0; i < lines.Length; i++)
        {
            if (GoDirective().Match(lines[i]) is { Success: true } match)
            {
                return [("go", false, VersionLine(match.Groups["version"].Value), new Evidence(file.Path, i + 1, lines[i].Trim()))];
            }
        }

        return [];
    }

    private static string? ReadJson(FileContent file, Func<JsonElement, string?> read)
    {
        try
        {
            using var document = JsonDocument.Parse(file.Content, JsonOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object ? read(document.RootElement) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Image and tag; registry port and digest are kept apart (<c>host:5000/app:1.2@sha256:…</c>).</summary>
    private static (string Image, string? Tag) SplitImage(string reference)
    {
        var withoutDigest = reference.Split('@')[0];
        var slash = withoutDigest.LastIndexOf('/');
        var colon = withoutDigest.LastIndexOf(':');
        return colon > slash ? (withoutDigest[..colon], withoutDigest[(colon + 1)..]) : (withoutDigest, null);
    }

    private static string[] Lines(string content) => content.ReplaceLineEndings("\n").Split('\n');

    private static int LineOf(string content, string value)
    {
        var lines = Lines(content);
        var index = Array.FindIndex(lines, l => l.Contains(value, StringComparison.Ordinal));
        return index < 0 ? 1 : index + 1;
    }

    private static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];

    [GeneratedRegex(@"^(docker-)?compose(\.[\w.-]+)?\.ya?ml$", RegexOptions.IgnoreCase)]
    private static partial Regex ComposeName();

    [GeneratedRegex(@"^ARG\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(=(?<value>\S+))?", RegexOptions.IgnoreCase)]
    private static partial Regex ArgLine();

    [GeneratedRegex(@"^FROM\s+(--\S+\s+)*(?<image>\S+)(\s+AS\s+(?<stage>\S+))?", RegexOptions.IgnoreCase)]
    private static partial Regex FromLine();

    [GeneratedRegex(@"\$\{?(?<name>[A-Za-z_][A-Za-z0-9_]*)\}?")]
    private static partial Regex VariableReference();

    [GeneratedRegex(@"^\s*-?\s*image:\s*[""']?(?<image>[^""'\s#]+)")]
    private static partial Regex ComposeImage();

    [GeneratedRegex(@"^v?(?<line>\d+(\.\d+)?)")]
    private static partial Regex LeadingVersion();

    [GeneratedRegex(@"\d+(\.\d+)*")]
    private static partial Regex RangeStart();

    [GeneratedRegex(@"^net(?<line>\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ModernFramework();

    [GeneratedRegex(@"^net(?<digits>[1-4]\d{1,2})$", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyFramework();

    [GeneratedRegex(@"^go\s+(?<version>\d+(\.\d+)*)\s*$")]
    private static partial Regex GoDirective();
}
