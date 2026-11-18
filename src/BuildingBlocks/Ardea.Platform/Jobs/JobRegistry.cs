using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Jobs;

/// <summary>Every job kind the process knows, with its options and how to invoke its handler.</summary>
public sealed class JobRegistry
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, Registration> _kinds = new(StringComparer.Ordinal);

    internal IReadOnlyCollection<Registration> Kinds => _kinds.Values;

    internal Registration this[string kind] => _kinds[kind];

    internal bool TryGet(string kind, out Registration registration) => _kinds.TryGetValue(kind, out registration!);

    internal void Add<TJob, THandler>(JobKindOptions options)
        where TJob : IJob
        where THandler : class, IJobHandler<TJob>
    {
        if (!_kinds.TryAdd(TJob.Kind, new Registration(TJob.Kind, options, Invoke)))
        {
            throw new InvalidOperationException($"Job kind '{TJob.Kind}' is registered twice.");
        }

        static Task Invoke(IServiceProvider services, JsonDocument payload, JobContext context, CancellationToken cancellationToken)
        {
            var job = payload.Deserialize<TJob>(Json)
                ?? throw new InvalidOperationException($"Job {context.JobId} of kind {TJob.Kind} has an empty payload.");
            return services.GetRequiredService<THandler>().HandleAsync(job, context, cancellationToken);
        }
    }

    internal sealed record Registration(
        string Kind,
        JobKindOptions Options,
        Func<IServiceProvider, JsonDocument, JobContext, CancellationToken, Task> Invoke);
}

public static class JobServiceCollectionExtensions
{
    /// <summary>Registers a job kind with its handler. The handler is resolved from the job's own scope.</summary>
    public static IServiceCollection AddJob<TJob, THandler>(this IServiceCollection services, JobKindOptions? options = null)
        where TJob : IJob
        where THandler : class, IJobHandler<TJob>
    {
        services.AddScoped<THandler>();
        GetRegistry(services).Add<TJob, THandler>(options ?? new JobKindOptions());
        return services;
    }

    internal static JobRegistry GetRegistry(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(JobRegistry));
        if (descriptor?.ImplementationInstance is JobRegistry existing)
        {
            return existing;
        }

        var registry = new JobRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}
