using System.Text.Json;
using Ardea.Catalog.Contracts;

namespace Ardea.Assets.Repositories.Packages;

/// <summary>Shipped or only for building and testing (master plan 4.5, 16.5). Without the information, a package counts as shipped.</summary>
internal enum DependencyScope
{
    Unknown,
    Runtime,
    Development,
}

/// <summary>Declared by the project itself, or pulled in by another package (master plan 4.5, 16.5).</summary>
internal enum DependencyRelation
{
    Unknown,
    Direct,
    Transitive,
}

/// <summary>Where a finding comes from.</summary>
internal enum FindingSource
{
    /// <summary>GitHub's dependency graph; it names no file.</summary>
    Sbom,

    /// <summary>A lockfile Ardea read itself.</summary>
    Lockfile,
}

/// <summary>The evidence of a finding: the file, and the line where it was cheap to find (master plan 4.5: «Datei und Zeile»).</summary>
internal sealed record FindingEvidence(FindingSource Source, string? Path, int? Line = null);

/// <summary>
/// A package a repository uses, as one reader saw it (master plan 16.3). <see cref="Package"/> is the
/// purl type and the normalised name (<see cref="PackageCoordinate.Normalize"/>). <see cref="Version"/> is
/// <c>null</c> when it is unknown or only a range (<c>^4.17.1</c>, <c>&gt;= 0</c>); <see cref="Exact"/>
/// says it is the version actually resolved, not just a lower bound.
/// </summary>
internal sealed record PackageFinding(
    PackageCoordinate Package,
    string? Version,
    bool Exact,
    DependencyScope Scope,
    DependencyRelation Relation,
    FindingEvidence Evidence)
{
    /// <summary>The package URL with version, e.g. <c>pkg:npm/%40angular/core@22.2.1</c>; without version when it is unknown.</summary>
    public string Purl => Version is null
        ? Package.ToString()
        : $"pkg:{Package.Type}/{string.Join('/', Package.Name.Split('/').Select(Uri.EscapeDataString))}@{Uri.EscapeDataString(Version)}";
}

/// <summary>What a lockfile reader produced, or why it produced nothing. A malformed file is a result, not a crash.</summary>
internal sealed record PackageReadResult(IReadOnlyList<PackageFinding> Findings, string? Error)
{
    public bool Ok => Error is null;

    public static PackageReadResult Failed(string error) => new([], error);
}

/// <summary>Bounds shared by the readers (master plan 16.9): they read data, never execute it.</summary>
internal static class PackageReaders
{
    /// <summary>The largest lockfile a reader accepts; the indexing job reads at most 1 MB per file anyway (16.3).</summary>
    public const int MaxLockfileBytes = 32 * 1024 * 1024;

    /// <summary>Lockfiles are nested (npm v1 one level per dependency path), but never this deep.</summary>
    public static readonly JsonDocumentOptions Document = new() { MaxDepth = 128 };

    /// <summary>
    /// Whether <paramref name="version"/> names one version rather than a range: not empty, no operator
    /// (<c>^ ~ &gt; &lt; = | ,</c>), no wildcard (<c>*</c>, <c>x</c>), no whitespace, no interval bracket,
    /// no URL or path, and not a tag such as <c>latest</c>.
    /// </summary>
    public static bool IsPlainVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 128)
        {
            return false;
        }

        if (version.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || "^~<>=|,*[]()/:\\".Contains(c, StringComparison.Ordinal)))
        {
            return false;
        }

        var trimmed = version.TrimStart('v', 'V');
        if (trimmed.Length == 0 || !char.IsAsciiDigit(trimmed[0]) && !IsHex(trimmed))
        {
            // "latest", "next", "NOASSERTION"; a commit SHA (GitHub Actions) is a version.
            return false;
        }

        // A wildcard component: 1.x, 1.2.X
        return !trimmed.Split('.', '-', '+').Any(part => part is "x" or "X");
    }

    /// <summary>The 1-based line of the first <c>"key": {</c> for every key <paramref name="wanted"/> accepts; a single pass, no parse.</summary>
    public static Dictionary<string, int> ObjectKeyLines(ReadOnlySpan<byte> utf8, Func<string, bool> wanted)
    {
        var lines = new Dictionary<string, int>(StringComparer.Ordinal);
        var line = 1;
        var start = 0;
        while (start <= utf8.Length)
        {
            var end = utf8[start..].IndexOf((byte)'\n');
            var text = end < 0 ? utf8[start..] : utf8.Slice(start, end);
            var open = text.IndexOf((byte)'"');
            if (open >= 0)
            {
                var rest = text[(open + 1)..];
                var close = rest.IndexOf((byte)'"');
                if (close > 0 && rest[(close + 1)..].TrimStart(" \t"u8) is [(byte)':', .. var value] && value.TrimStart(" \t"u8) is [(byte)'{', ..])
                {
                    var key = System.Text.Encoding.UTF8.GetString(rest[..close]);
                    if (wanted(key))
                    {
                        lines.TryAdd(key, line);
                    }
                }
            }

            if (end < 0)
            {
                break;
            }

            start += end + 1;
            line++;
        }

        return lines;
    }

    private static bool IsHex(string text) => text.Length >= 7 && text.All(char.IsAsciiHexDigit);
}
