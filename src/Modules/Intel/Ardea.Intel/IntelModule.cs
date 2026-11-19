using Ardea.Intel.Http;
using Ardea.Intel.Persistence;
using Ardea.Intel.Sync;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ardea.Intel;

public sealed class IntelModule : IModule
{
    public string Name => IntelDbContext.Schema;

    public IModuleDatabase? Database { get; } =
        new ModuleDatabase<IntelDbContext>(IntelDbContext.Schema, options => new IntelDbContext(options, new TenantContext()));

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IntelDbContext>(IntelDbContext.Schema);
        services.AddSingleton<SourceRegistry>();
        services.AddSingleton<HostRateLimits>();
        services.AddSingleton<SourceHttpFactory>();
        services.AddScoped<Contracts.IEntryQueries, Features.EntryQueries>();

        services.AddHttpClient(SourceHttp.ClientName)
            .ConfigurePrimaryHttpMessageHandler(PrimaryHandler)
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2);
                o.Retry.MaxRetryAttempts = 3;
            });
        services.AddHttpClient(SourceHttp.BackfillClientName)
            .ConfigurePrimaryHttpMessageHandler(PrimaryHandler)
            .AddStandardResilienceHandler(o =>
            {
                // A full archive is large; the run's own time limit bounds it.
                o.AttemptTimeout.Timeout = TimeSpan.FromMinutes(30);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(55);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromHours(1);
                o.Retry.MaxRetryAttempts = 2;
            });

        services.AddJob<ScheduleDueSources, ScheduleDueSourcesHandler>(new JobKindOptions { MaxConcurrency = 1, MaxAttempts = 1, Priority = JobPriority.Urgent, Timeout = TimeSpan.FromMinutes(1) });
        services.AddJob<SyncSource, SyncSourceHandler>(new JobKindOptions { MaxConcurrency = 4, MaxAttempts = 1, Timeout = SyncSource.Timeout });
        services.AddJob<NormalizeRecords, NormalizeRecordsHandler>(new JobKindOptions { MaxConcurrency = 2, MaxAttempts = 3, Timeout = TimeSpan.FromMinutes(30) });
        services.AddJob<Correlation.CorrelateEntry, Correlation.CorrelateEntryHandler>(new JobKindOptions { MaxConcurrency = 4, MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(2) });
        services.AddJob<Correlation.NotifyEntryChanged, Correlation.NotifyEntryChangedHandler>(new JobKindOptions { MaxConcurrency = 4, MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(2) });
        services.AddJob<CheckCompleteness, CheckCompletenessHandler>(new JobKindOptions { MaxConcurrency = 1, MaxAttempts = 1, Timeout = TimeSpan.FromMinutes(30), Priority = JobPriority.Background });
        services.AddSchedule("intel.schedule-due-sources", "* * * * *", new ScheduleDueSources());
        services.AddSchedule("intel.check-completeness", "23 4 * * *", new CheckCompleteness());
    }

    /// <summary>Only the worker fetches; it registers every adapter's source at start.</summary>
    public void AddWorkerServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton<IHostedService, SourceRegistrar>();

    public IReadOnlyList<MaintenanceCommand> Commands { get; } = [Features.IntelCommands.Renormalize, Features.IntelCommands.Recompute, Features.IntelCommands.ImportFull];

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.EntryEndpoints.Map(endpoints);
        Features.SourceEndpoints.Map(endpoints);
        Features.ReviewEndpoints.Map(endpoints);
    }

    private static HttpMessageHandler PrimaryHandler() => new SocketsHttpHandler
    {
        // Redirects are followed by the guard, which checks every target host.
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(15),
    };
}
