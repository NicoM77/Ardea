using Ardea.Assessment.Assessing;
using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assessment;

/// <summary>
/// Candidate search against the profiles, version check, rule set, findings, actions and alerts
/// (master plan 3.2, 7). Hears about changes through the subscriber interfaces of Intel and Assets.
/// </summary>
public sealed class AssessmentModule : IModule
{
    public string Name => AssessDbContext.Schema;

    public IModuleDatabase? Database { get; } =
        new ModuleDatabase<AssessDbContext>(AssessDbContext.Schema, options => new AssessDbContext(options, new TenantContext()));

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AssessDbContext>(AssessDbContext.Schema);
        // The rule set is a file in the image (master plan 4.9); read once, checked on first use.
        services.AddSingleton(_ => Rules.RuleSet.Load(MasterDataOptions.FromConfiguration(configuration).Directory));
        services.AddScoped<AlertAssessor>();
        services.AddScoped<IEntryChangeSubscriber, EntryChangeSubscriber>();
        services.AddScoped<IProfileChangeSubscriber, ProfileChangeSubscriber>();
        services.AddJob<MatchEntry, MatchEntryHandler>(new JobKindOptions { MaxConcurrency = 4, MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(2) });
        services.AddJob<AssessAlert, AssessAlertHandler>(new JobKindOptions { MaxConcurrency = 4, MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(2) });
        services.AddJob<MatchProject, MatchProjectHandler>(new JobKindOptions { MaxConcurrency = 2, MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(10) });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
