using Ardea.Catalog.Contracts;
using Ardea.Catalog.Products;
using Ardea.Catalog.Versions;
using Ardea.Platform.Persistence;

namespace Ardea.Catalog.Tests.Products;

/// <summary>The curated catalogue in <c>data/catalog/</c> loads without error (what <c>migrate</c> requires).</summary>
public sealed class CatalogFilesTests
{
    public static string RepositoryCatalog => Path.Combine(MasterDataOptions.FindDefault(), CatalogFiles.Folder);

    [Fact]
    public void Every_catalogue_file_loads_and_validates()
    {
        var load = CatalogFiles.Load(RepositoryCatalog);

        var errors = load.Errors.Concat(CatalogValidator.Validate(load.Products, VersionSchemeRegistry.Instance)).ToList();

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        Assert.NotEmpty(load.Products);
    }

    [Fact]
    public void Examples_cover_every_kind_and_every_scheme()
    {
        var products = CatalogFiles.Load(RepositoryCatalog).Products;

        Assert.Empty(Enum.GetValues<ProductKind>().Except(products.Select(p => p.Kind)));
        Assert.Empty(VersionSchemeRegistry.Instance.All.Select(s => s.Name).Except(products.Select(p => p.VersionScheme)));
    }

    [Fact]
    public void Broken_files_are_reported_with_file_and_product()
    {
        var directory = TempCatalog.Write(new()
        {
            ["acme.json"] = """
                { "vendor": "Acme", "products": [
                  { "key": "tool", "name": "Tool", "kind": "gadget", "versionScheme": "semver" },
                  { "key": "lib", "name": "Lib", "kind": "library", "versionScheme": "semver", "cpe": ["acme"], "packages": ["pkg:npm/lib@1.0.0"] },
                ] }
                """,
            ["typo.json"] = """
                { "vendor": "Typo", "products": [{ "key": "other", "name": "Other", "kind": "library", "versionScheme": "semver", "colour": "red" }] }
                """,
        });

        var load = CatalogFiles.Load(directory);

        Assert.Contains(load.Errors, e => e.Contains("acme.json, tool: unknown kind 'gadget'", StringComparison.Ordinal));
        Assert.Contains(load.Errors, e => e.Contains("'acme' is not a CPE vendor:product pair", StringComparison.Ordinal));
        Assert.Contains(load.Errors, e => e.Contains("'pkg:npm/lib@1.0.0' is not a package URL without version", StringComparison.Ordinal));
        Assert.Contains(load.Errors, e => e.StartsWith("typo.json:", StringComparison.Ordinal) && e.Contains("colour", StringComparison.Ordinal));
        Assert.Empty(load.Products);
    }

    [Fact]
    public void Missing_or_empty_folder_is_an_error()
    {
        Assert.NotEmpty(CatalogFiles.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).Errors);
        Assert.NotEmpty(CatalogFiles.Load(TempCatalog.Write([])).Errors);
    }

    [Fact]
    public void Validator_finds_duplicates_unknown_schemes_and_dangling_keys()
    {
        CatalogProduct[] products =
        [
            new() { Key = "tool", Name = "Tool", Kind = ProductKind.Library, VersionScheme = "semver", CpePairs = [new("acme", "tool")], ImageNames = ["acme/tool"] },
            new() { Key = "tool2", Name = "Tool", Kind = ProductKind.Library, VersionScheme = "debian", CpePairs = [new("acme", "tool")], RelatedKeys = ["nothing"] },
            new() { Key = "tool", Name = "Again", Kind = ProductKind.Library, VersionScheme = "semver" },
            new() { Key = "Plug In", Name = "Plug-in", Kind = ProductKind.Plugin, VersionScheme = "numeric", ImageNames = ["docker.io/library/acme:1.0"] },
            new() { Key = "theme", Name = "Theme", Kind = ProductKind.Plugin, VersionScheme = "numeric", PlatformKey = "theme", Aliases = [new(null, ["Bad {version}"])] },
        ];

        var errors = CatalogValidator.Validate(products, VersionSchemeRegistry.Instance);

        Assert.Contains(errors, e => e.Contains("key 'tool' is used twice", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("CPE pair 'acme:tool' belongs to 'tool' and 'tool2'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("spelling 'Tool'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("unknown version scheme 'debian'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("related product 'nothing'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'Plug In': a key has", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'Plug In': a plugin names its platform", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'Plug In': image names", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("platform 'theme' is not another product", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("{version}", StringComparison.Ordinal));
    }
}

internal static class TempCatalog
{
    /// <summary>A fresh folder with the given files; returns its path.</summary>
    public static string Write(Dictionary<string, string> files)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ardea-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var (name, content) in files)
        {
            File.WriteAllText(Path.Combine(directory, name), content);
        }

        return directory;
    }
}
