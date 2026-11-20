using Ardea.Catalog.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ardea.Catalog.Features;

internal sealed record CatalogProductView(string Key, string Name, string? Vendor, string Kind, string VersionScheme, string? PlatformKey);

internal static class CatalogEndpoints
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/catalog/products", SearchAsync).WithTags("Catalog").WithName("searchCatalog").RequireAuthorization();

    private static async Task<IReadOnlyList<CatalogProductView>> SearchAsync(string? q, int? limit, IProductCatalog catalog, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return [];
        }

        var products = await catalog.SearchAsync(q.Trim(), Math.Clamp(limit ?? 20, 1, 50), cancellationToken);
        return [.. products.Select(p => new CatalogProductView(p.Key, p.Name, p.Vendor, p.Kind.ToString().ToLowerInvariant(), p.VersionScheme, p.PlatformKey))];
    }
}
