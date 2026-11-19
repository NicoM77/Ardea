using Ardea.Assets.Features;
using Ardea.Assets.Persistence;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assets;

/// <summary>Projects, components and, from task 2.10 on, the repositories that feed them (master plan 3.2, 4.5, 16).</summary>
public sealed class AssetsModule : IModule
{
    public string Name => AssetsDbContext.Schema;

    public IModuleDatabase? Database { get; } =
        new ModuleDatabase<AssetsDbContext>(AssetsDbContext.Schema, options => new AssetsDbContext(options, new TenantContext()));

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AssetsDbContext>(AssetsDbContext.Schema);
        services.AddJob<ProjectProfileChanged, ProjectProfileChangedHandler>(new JobKindOptions { MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(1) });
        services.AddScoped<GitHub.GitHubHosts>();
        services.AddScoped<GitHub.GitHubApi>();
        services.AddHttpClient(GitHub.GitHubApi.ClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(Platform.Http.OutboundAddresses.CreateHandler);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ProjectEndpoints.Map(endpoints);
        ConnectionEndpoints.Map(endpoints);
        LinkEndpoints.Map(endpoints);
    }
}
