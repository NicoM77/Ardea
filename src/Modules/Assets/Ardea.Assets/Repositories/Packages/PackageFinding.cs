using System.Text.Json;
using Ardea.Catalog.Contracts;

namespace Ardea.Assets.Repositories.Packages;

internal enum DependencyScope
{
    Unknown,
    Runtime,
    Development,
}

internal enum DependencyRelation
{
    Unknown,
    Direct,
    Transitive,
}

internal enum FindingSource
{
    Sbom,

    Lockfile,
}

internal sealed record FindingEvidence(FindingSource Source, string? Path, int? Line = null);

internal sealed record PackageFinding(
    PackageCoordinate Package,
    string? Version,
    bool Exact,
    DependencyScope Scope,
    DependencyRelation Relation,
    FindingEvidence Evidence)
{
    public string Purl => Version is null
        ? Package.ToString()
        : $"pkg:{Package.Type}/{string.Join('/', Package.Name.Split('/').Select(Uri.EscapeDataString))}@{Uri.EscapeDataString(Version)}";
}

internal sealed record PackageReadResult(IReadOnlyList<PackageFinding> Findings, string? Error)
{
    public bool Ok => Error is null;

    public static PackageReadResult Failed(string error) => new([], error);
}

internal static class PackageReaders
{
    public const int MaxLockfileBytes = 32 * 1024 * 1024;

    public static readonly JsonDocumentOptions Document = new() { MaxDepth = 128 };

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
            return false;
        }

        return !trimmed.Split('.', '-', '+').Any(part => part is "x" or "X");
    }

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
