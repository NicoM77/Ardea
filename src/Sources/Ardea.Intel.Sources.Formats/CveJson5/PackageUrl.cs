namespace Ardea.Intel.Sources.Formats.CveJson5;

public static class PackageUrl
{
    public static string? From(string? collectionUrl, string? packageName)
    {
        if (string.IsNullOrWhiteSpace(collectionUrl) || string.IsNullOrWhiteSpace(packageName)
            || !Uri.TryCreate(collectionUrl.Trim(), UriKind.Absolute, out var collection))
        {
            return null;
        }

        var name = packageName.Trim();
        var type = collection.Host.ToLowerInvariant() switch
        {
            "registry.npmjs.org" or "www.npmjs.com" or "npmjs.com" => "npm",
            "pypi.org" or "pypi.python.org" => "pypi",
            "www.nuget.org" or "nuget.org" or "api.nuget.org" => "nuget",
            "packagist.org" or "repo.packagist.org" => "composer",
            "repo.maven.apache.org" or "repo1.maven.org" or "central.sonatype.com" or "search.maven.org" => "maven",
            "pkg.go.dev" or "proxy.golang.org" => "golang",
            "crates.io" => "cargo",
            "rubygems.org" => "gem",
            _ => null,
        };

        return type switch
        {
            null => null,
            "maven" when name.Split(':') is [var group, var artifact] => $"pkg:maven/{group}/{artifact}",
            "maven" => null,
            "npm" when name.StartsWith('@') => $"pkg:npm/%40{name[1..]}",
            "pypi" => $"pkg:pypi/{name.ToLowerInvariant().Replace('_', '-')}",
            _ => $"pkg:{type}/{name}",
        };
    }
}
