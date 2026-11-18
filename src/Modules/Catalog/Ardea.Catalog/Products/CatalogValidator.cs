using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Products;

/// <summary>
/// Checks the curated catalogue before it is loaded: unique keys, spellings, CPE pairs, packages and
/// images; known version schemes; platform and related keys that exist. <c>migrate</c> refuses a
/// catalogue with errors, and a test runs the same check on every pull request.
/// </summary>
internal static class CatalogValidator
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<CatalogProduct> products, IVersionSchemes schemes)
    {
        var errors = new List<string>();
        var index = new CatalogIndex(products);
        errors.AddRange(index.Conflicts);

        var schemeNames = schemes.All.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var product in products)
        {
            var where = $"'{product.Key}'";
            if (!ProductKey.TryParse(product.Key, out _))
            {
                errors.Add($"{where}: a key has lower-case letters and digits, joined by single '-' or '.', with an optional vendor part before '/'");
            }

            if (!schemeNames.Contains(product.VersionScheme))
            {
                errors.Add($"{where}: unknown version scheme '{product.VersionScheme}'; known are {string.Join(", ", schemeNames.Order(StringComparer.Ordinal))}");
            }

            if (product.Kind == ProductKind.Plugin && product.PlatformKey is null)
            {
                errors.Add($"{where}: a plugin names its platform");
            }

            if (product.PlatformKey is { } platform && (platform == product.Key || index.FindByKey(platform) is null))
            {
                errors.Add($"{where}: platform '{platform}' is not another product of the catalogue");
            }

            foreach (var related in product.RelatedKeys)
            {
                if (related == product.Key || index.FindByKey(related) is null)
                {
                    errors.Add($"{where}: related product '{related}' is not another product of the catalogue");
                }
            }

            foreach (var alias in product.Aliases)
            {
                if (alias.Names.Count == 0 || alias.Names.Any(string.IsNullOrWhiteSpace))
                {
                    errors.Add($"{where}: an alias lists at least one name and no empty one");
                }
            }

            if (product.ImageNames.Any(i => CatalogIndex.NormalizeImage(i) != i))
            {
                errors.Add($"{where}: image names are written without registry docker.io, 'library/', tag or digest, in lower case");
            }
        }

        return errors;
    }
}
