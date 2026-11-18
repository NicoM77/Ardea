using Ardea.Catalog.Persistence;
using Ardea.Catalog.Versions;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Catalog.Products;

/// <summary>
/// Loads the curated catalogue into <c>catalog.product</c> on every <c>migrate</c> (master plan 4.11).
/// Idempotent: curated rows are inserted, updated or removed to match the files. Learned rows are left
/// alone (master plan 6.9), with one exception: a curated file that takes over the key of a learned
/// entry promotes it to curated; this is how learned entries return to the project.
/// </summary>
internal static class CatalogSeeder
{
    public static async Task<int> SeedAsync(CatalogDbContext context, MasterDataOptions masterData, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(masterData.Directory, CatalogFiles.Folder);
        var load = CatalogFiles.Load(directory);
        var errors = load.Errors.Concat(CatalogValidator.Validate(load.Products, VersionSchemeRegistry.Instance)).ToList();
        if (errors.Count > 0)
        {
            // An empty or broken folder must never wipe the catalogue.
            throw new InvalidOperationException($"The catalogue in '{directory}' cannot be loaded:{Environment.NewLine}  - {string.Join(Environment.NewLine + "  - ", errors)}");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var rows = await context.Products.ToDictionaryAsync(p => p.Key, StringComparer.Ordinal, cancellationToken);
        var now = TimeProvider.System.GetUtcNow();
        var changed = 0;

        foreach (var product in load.Products)
        {
            if (!rows.TryGetValue(product.Key, out var row))
            {
                row = new ProductRow
                {
                    Key = product.Key,
                    Name = product.Name,
                    Kind = string.Empty,
                    VersionScheme = product.VersionScheme,
                    Origin = ProductRow.Curated,
                    State = ProductRow.Active,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                ProductRows.WriteCurated(product, row);
                context.Products.Add(row);
                changed++;
                continue;
            }

            ProductRows.WriteCurated(product, row);
            context.ChangeTracker.DetectChanges();
            if (context.Entry(row).State == EntityState.Modified)
            {
                row.UpdatedAt = now;
                changed++;
            }
        }

        var curatedKeys = load.Products.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows.Values.Where(r => r.Origin == ProductRow.Curated && !curatedKeys.Contains(r.Key)))
        {
            context.Products.Remove(row);
            changed++;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }
}
