namespace Ardea.Intel.Sources.Formats.Osv;

public static class OsvEcosystems
{
    public static readonly IReadOnlyList<string> Mvp = ["npm", "NuGet", "Packagist", "PyPI", "Maven", "Go"];

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
