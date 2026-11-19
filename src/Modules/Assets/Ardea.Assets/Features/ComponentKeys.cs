using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assets.Features;

/// <summary>
/// Keys under which an entry can hit a component, with their strength (master plan 7.2):
/// <list type="bullet">
/// <item>A catalogue product: its key, its CPE pairs and its packages are strong; related but
/// independent products (MariaDB for MySQL) are weak.</item>
/// <item>A product by name: through the catalogue's aliases it is that product (strong); otherwise its
/// raw key, strong with a vendor and weak with the name alone. A name known only through a learned entry
/// keeps its raw key besides.</item>
/// <item>A package from a repository: its package is strong, and the product the catalogue knows for it.</item>
/// </list>
/// Hits on a platform through one of its plugins are decided at matching time, from the entry's side.
/// </summary>
internal static class ComponentKeys
{
    public static async Task<IReadOnlyList<(string Type, string Value, string Strength)>> ComputeAsync(
        Component component, IProductCatalog catalog, CancellationToken cancellationToken)
    {
        var keys = new Dictionary<(string, string), string>();
        void Add(string type, string value, string strength)
        {
            // A strong key never weakens because the same value also comes as a weak one.
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

        if (component.Purl is not null && PackageCoordinate.TryParsePurl(component.Purl, out var coordinate, out _))
        {
            Add("purl", coordinate.ToString(), "strong");
            if (component.ProductKey is null && await catalog.FindByPackageAsync(coordinate.Type, coordinate.Name, cancellationToken) is { } byPackage)
            {
                AddProduct(byPackage);
            }
        }

        if (component.ProductKey is not null)
        {
            if (await catalog.FindByKeyAsync(component.ProductKey, cancellationToken) is { } product)
            {
                AddProduct(product);
            }
            else
            {
                // A learned entry that is no longer in effect: its key still finds raw mentions.
                Add("product", component.ProductKey, "strong");
            }
        }
        else if (component.CustomName is not null)
        {
            if (await catalog.FindByNameAsync(component.CustomVendor, component.CustomName, cancellationToken) is { } match)
            {
                AddProduct(match.Product);

                // Known only through a learned entry: the raw key stays, so taking the entry back loses no hit (master plan 6.9).
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

    /// <summary>Writes the keys of the given components anew; call after the components are saved.</summary>
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
