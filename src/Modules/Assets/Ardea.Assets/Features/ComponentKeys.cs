using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assets.Features;

internal static class ComponentKeys
{
    public static async Task<IReadOnlyList<(string Type, string Value, string Strength)>> ComputeAsync(
        Component component, IProductCatalog catalog, CancellationToken cancellationToken)
    {
        var keys = new Dictionary<(string, string), string>();
        void Add(string type, string value, string strength)
        {
            if (!keys.TryGetValue((type, value), out var existing) || (existing == "weak" && strength == "strong"))
            {
                keys[(type, value)] = strength;
            }
        }

        void AddProduct(CatalogProduct product)
        {
            Add("product", product.Key, "strong");
            foreach (var pair in product.CpePairs)
            {
                Add("cpe", pair.ToString(), "strong");
            }

            foreach (var package in product.Packages)
            {
                Add("purl", package.ToString(), "strong");
            }

            foreach (var related in product.RelatedKeys)
            {
                Add("product", related, "weak");
            }
        }

        async Task AddLearnedFromAsync(string key)
        {
            foreach (var raw in await catalog.LearnedFromAsync(key, cancellationToken))
            {
                Add("product", raw, raw.Contains('/', StringComparison.Ordinal) ? "strong" : "weak");
            }
        }

        async Task AddProductAsync(CatalogProduct product)
        {
            AddProduct(product);
            if (product.Origin == ProductOrigin.Learned)
            {
                await AddLearnedFromAsync(product.Key);
            }
        }

        if (component.Purl is not null && PackageCoordinate.TryParsePurl(component.Purl, out var coordinate, out _))
        {
            Add("purl", coordinate.ToString(), "strong");
            if (component.ProductKey is null && await catalog.FindByPackageAsync(coordinate.Type, coordinate.Name, cancellationToken) is { } byPackage)
            {
                await AddProductAsync(byPackage);
            }
        }

        if (component.ProductKey is not null)
        {
            if (await catalog.FindByKeyAsync(component.ProductKey, cancellationToken) is { } product)
            {
                await AddProductAsync(product);
            }
            else
            {
                Add("product", component.ProductKey, "strong");
                await AddLearnedFromAsync(component.ProductKey);
            }
        }
        else if (component.CustomName is not null)
        {
            if (await catalog.FindByNameAsync(component.CustomVendor, component.CustomName, cancellationToken) is { } match)
            {
                await AddProductAsync(match.Product);

                if ((match.Learned || match.Product.Origin == ProductOrigin.Learned) && ProductKey.TryCreate(component.CustomVendor, component.CustomName, out var learnedRaw))
                {
                    Add("product", learnedRaw.Value, learnedRaw.Vendor is null ? "weak" : "strong");
                }
            }
            else if (ProductKey.TryCreate(component.CustomVendor, component.CustomName, out var raw))
            {
                Add("product", raw.Value, raw.Vendor is null ? "weak" : "strong");
            }
        }

        return [.. keys.Select(k => (k.Key.Item1, k.Key.Item2, k.Value))];
    }

    public static async Task RefreshAsync(AssetsDbContext db, IProductCatalog catalog, IReadOnlyCollection<Component> components, CancellationToken cancellationToken)
    {
        if (components.Count == 0)
        {
            return;
        }

        var ids = components.Select(c => c.Id).ToArray();
        await db.ComponentKeys.Where(k => ids.Contains(k.ComponentId)).ExecuteDeleteAsync(cancellationToken);
        foreach (var component in components)
        {
            foreach (var (type, value, strength) in await ComputeAsync(component, catalog, cancellationToken))
            {
                db.ComponentKeys.Add(new ComponentKey
                {
                    Id = Guid.CreateVersion7(),
                    ProjectId = component.ProjectId,
                    ComponentId = component.Id,
                    KeyType = type,
                    KeyValue = value.Length <= 500 ? value : value[..500],
                    Strength = strength,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
