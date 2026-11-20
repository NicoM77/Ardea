using Ardea.Catalog.Contracts;
using Ardea.Catalog.Learning;
using Ardea.Catalog.Persistence;
using Ardea.Catalog.Products;
using Ardea.Catalog.Versions;
using Ardea.Platform.Ai;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ardea.Catalog;

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
        services.AddScoped<ProductCatalog>();
        services.AddScoped<IProductCatalog>(sp => sp.GetRequiredService<ProductCatalog>());
        services.AddSingleton<IVersionSchemes>(VersionSchemeRegistry.Instance);

        services.TryAddSingleton<IAiProviders, NoAiProviders>();
        services.TryAddSingleton(AiLimits.FromConfiguration(configuration));
        services.AddSingleton(LearningOptions.FromConfiguration(configuration));
        services.AddScoped<Learner>();
        services.AddScoped<LearnedEntries>();
        services.AddScoped<ICatalogGrowth, CatalogGrowth>();
        services.AddJob<GrowCatalog, GrowCatalogHandler>(new JobKindOptions
        {
            MaxConcurrency = 1,
            MaxAttempts = 1,
            Timeout = TimeSpan.FromMinutes(60),
            Priority = JobPriority.Background,
        });
        services.AddSchedule("catalog.grow", "41 2 * * *", new GrowCatalog());
    }

    public IReadOnlyList<MaintenanceCommand> Commands { get; } = [Features.LearningCommands.Grow];

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.CatalogEndpoints.Map(endpoints);
        Features.LearningEndpoints.Map(endpoints);
    }
}
