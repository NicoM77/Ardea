using Ardea.Assessment.Assessing;
using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Platform.Ai;
using Ardea.Platform.Jobs;
using Ardea.Platform.Live;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddScoped<History>();
        services.AddScoped<Numbers>();
        services.AddScoped<Remediations>();
        services.AddScoped<Features.ActionReader>();
        services.AddScoped<Features.ActionCommands>();
        services.AddScoped<IEntryChangeSubscriber, EntryChangeSubscriber>();
        services.AddScoped<IProfileChangeSubscriber, ProfileChangeSubscriber>();
        services.AddJob<MatchEntry, MatchEntryHandler>(new JobKindOptions { MaxConcurrency = 4, MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(2) });
        services.AddJob<AssessAlert, AssessAlertHandler>(new JobKindOptions { MaxConcurrency = 4, MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(2) });
        services.AddJob<MatchProject, MatchProjectHandler>(new JobKindOptions { MaxConcurrency = 2, MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(10) });

        // The assessment with a language model (master plan 7.5, 7.8; phase 4). Without providers it never runs.
        var limits = AiLimits.FromConfiguration(configuration);
        services.TryAddSingleton<IAiProviders, Estimating.NoAiProviders>();
        services.AddSingleton(limits);
        services.AddScoped<Estimating.Estimator>();
        services.AddScoped<Estimating.EstimateTrigger>();
        services.AddScoped<Estimating.EstimatePlanner>();
        services.AddJob<Estimating.EstimateAlert, Estimating.EstimateAlertHandler>(new JobKindOptions
        {
            MaxConcurrency = limits.Parallelism,
            // One attempt: a second run of the job would ask the model again (ADR 037).
            MaxAttempts = 1,
            Timeout = (limits.CallTimeout * Estimating.Estimator.Attempts) + TimeSpan.FromMinutes(2),
        });
    }

    /// <summary>The live stream needs the listener for signals, in the API role only.</summary>
    public void AddApiServices(IServiceCollection services, IConfiguration configuration) => services.AddOrganizationSignals();

    public IReadOnlyList<MaintenanceCommand> Commands { get; } = [Features.AssessCommands.Recompute];

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ActionEndpoints.Map(endpoints);
        Features.LiveEndpoints.Map(endpoints);
    }
}
