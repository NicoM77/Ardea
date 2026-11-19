namespace Ardea.Intel.Sources.Formats.Osv;

/// <summary>
/// What the OSV reader needs to know about an ecosystem: its package URL type and the version scheme
/// that compares its versions (master plan 6.6). The scheme names are those of the catalogue's
/// <c>VersionSchemeNames</c>; this assembly may not reference the catalogue, so they are repeated here.
/// </summary>
public static class OsvEcosystems
{
    /// <summary>The six ecosystems of the MVP (master plan 5.6, 16.3), as OSV names them.</summary>
    public static readonly IReadOnlyList<string> Mvp = ["npm", "NuGet", "Packagist", "PyPI", "Maven", "Go"];

    /// <summary>
    /// The version scheme of an ecosystem. Ecosystems the MVP cannot compare yet keep their own name in
    /// lower case (<c>crates.io</c>, <c>debian</c>), which no scheme answers, so their versions give
    /// «unknown» instead of a guess by the numeric fallback.
    /// </summary>
    public static string? Scheme(string? ecosystem) => Base(ecosystem)?.ToLowerInvariant() switch
    {
        null => null,
        "npm" => "semver",
        "nuget" => "nuget",
        "packagist" => "packagist",
        "pypi" => "pep440",
        "maven" => "maven",
        "go" => "go",
        var other => other,
    };

    /// <summary>The package URL type of an ecosystem (purl spec); <c>null</c> when there is none.</summary>
    public static string? PurlType(string? ecosystem) => Base(ecosystem)?.ToLowerInvariant() switch
    {
        "npm" => "npm",
        "nuget" => "nuget",
        "packagist" => "composer",
        "pypi" => "pypi",
        "maven" => "maven",
        "go" => "golang",
        "crates.io" => "cargo",
        "rubygems" => "gem",
        "hex" => "hex",
        "pub" => "pub",
        "hackage" => "hackage",
        "swifturl" => "swift",
        _ => null,
    };

    /// <summary>
    /// A package URL without version from ecosystem and name, for records that carry none
    /// (the GitHub Advisory Database writes no <c>purl</c>; OSV.dev adds it when it imports).
    /// </summary>
    public static string? PackageUrl(string? ecosystem, string? name)
    {
        var type = PurlType(ecosystem);
        var trimmed = name?.Trim();
        if (type is null || string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return type switch
        {
            "maven" when trimmed.Split(':') is [var group, var artifact] && group.Length > 0 && artifact.Length > 0 => $"pkg:maven/{group}/{artifact}",
            "maven" => null,
            "npm" when trimmed.StartsWith('@') => $"pkg:npm/%40{trimmed[1..]}",
            "pypi" => $"pkg:pypi/{string.Join('-', trimmed.ToLowerInvariant().Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries))}",
            _ => $"pkg:{type}/{trimmed}",
        };
    }

    /// <summary>OSV qualifies some ecosystems with a release, e.g. <c>Debian:12</c>; the part before the colon.</summary>
    private static string? Base(string? ecosystem)
    {
        var trimmed = ecosystem?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 ? trimmed[..colon] : trimmed;
    }
}
