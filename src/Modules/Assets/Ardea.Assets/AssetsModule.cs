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
        new ModuleDatabase<AssetsDbContext>(
            AssetsDbContext.Schema,
            options => new AssetsDbContext(options, new TenantContext()),
            seed: (db, _, cancellationToken) => StackProject.EnsureForEveryOrganizationAsync(db, cancellationToken));

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AssetsDbContext>(AssetsDbContext.Schema);
        services.AddScoped<Platform.Status.IInstanceStatusContributor, AssetsStatusContributor>();
        services.AddScoped<Contracts.IProfileQueries, ProfileQueries>();
        services.AddScoped<Iam.Contracts.IOrganizationCreatedSubscriber, StackProject>();
        services.AddSingleton<IDemoSeedStep, DemoSeed>();
        services.AddSingleton<IDemoSeedStep, DemoUpdate>();
        services.AddJob<ProjectProfileChanged, ProjectProfileChangedHandler>(new JobKindOptions { MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(1) });
        // One reading at a time per worker slot of four; a reading talks to GitHub and may wait for the SBOM.
        services.AddJob<Repositories.Indexing.IndexRepository, Repositories.Indexing.IndexRepositoryHandler>(
            new JobKindOptions { MaxConcurrency = 4, MaxAttempts = 2, Timeout = TimeSpan.FromMinutes(10) });
        services.AddJob<Repositories.Indexing.ScheduleDueRepositories, Repositories.Indexing.ScheduleDueRepositoriesHandler>(
            new JobKindOptions { MaxConcurrency = 1, MaxAttempts = 1, Priority = JobPriority.Urgent, Timeout = TimeSpan.FromMinutes(1) });
        services.AddSchedule("assets.schedule-due-repositories", "*/5 * * * *", new Repositories.Indexing.ScheduleDueRepositories());
        services.AddScoped<GitHub.GitHubHosts>();
        services.AddScoped<GitHub.GitHubApi>();
        services.AddScoped<Repositories.IRepositoryHost, Repositories.GitHubRepositoryHost>();
        services.AddHttpClient(GitHub.GitHubApi.ClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(Platform.Http.OutboundAddresses.CreateHandler);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ProjectEndpoints.Map(endpoints);
        StackSuggestions.Map(endpoints);
        ConnectionEndpoints.Map(endpoints);
        LinkEndpoints.Map(endpoints);
        CoverageEndpoints.Map(endpoints);
    }
}
