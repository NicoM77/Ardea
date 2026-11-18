using Ardea.Catalog.Contracts;
using Ardea.Catalog.Persistence;
using Ardea.Catalog.Products;
using Ardea.Catalog.Versions;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Catalog;

/// <summary>
/// The technology catalogue (master plan 3.2, 4.5): product keys, CPE reader, version schemes and the
/// curated products, loaded from <c>data/catalog/</c> by <c>migrate</c>.
/// </summary>
public sealed class CatalogModule : IModule
{
    public string Name => CatalogDbContext.Schema;

    public IModuleDatabase? Database { get; } = new ModuleDatabase<CatalogDbContext>(
        CatalogDbContext.Schema,
        options => new CatalogDbContext(options, new TenantContext()),
        seed: CatalogSeeder.SeedAsync);

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CatalogDbContext>(CatalogDbContext.Schema);
        services.AddSingleton<CatalogCache>();
        services.AddScoped<IProductCatalog, ProductCatalog>();
        services.AddSingleton<IVersionSchemes>(VersionSchemeRegistry.Instance);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
