using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ardea.Platform.Jobs;

public static class JobSystemServices
{
    public static IServiceCollection AddJobQueue(this IServiceCollection services)
    {
        JobServiceCollectionExtensions.GetRegistry(services);
        services.TryAddScoped<IJobQueue, JobQueue>();
        services.TryAddScoped<IJobAdministration, JobAdministration>();
        if (!services.Any(d => d.ServiceType == typeof(CleanUpJobsHandler)))
        {
            services.AddJob<CleanUpJobs, CleanUpJobsHandler>(new JobKindOptions { MaxConcurrency = 1, Priority = JobPriority.Background });
            services.AddSchedule("platform.clean-up-jobs", "17 3 * * *", new CleanUpJobs());
        }

        return services;
    }

    public static IServiceCollection AddJobWorker(this IServiceCollection services, JobWorkerOptions? options = null)
    {
        services.AddJobQueue();
        services.AddSingleton(options ?? new JobWorkerOptions());
        services.AddSingleton<JobProcessor>();
        services.AddHostedService(sp => sp.GetRequiredService<JobProcessor>());
        services.AddHostedService<ScheduleLeader>();
        return services;
    }
}
