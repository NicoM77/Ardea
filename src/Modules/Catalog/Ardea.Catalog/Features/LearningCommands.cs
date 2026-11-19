using Ardea.Catalog.Learning;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Catalog.Features;

/// <summary>Maintenance commands of the catalogue (master plan 12.8); the worker does the work through jobs.</summary>
internal static class LearningCommands
{
    /// <summary>
    /// <c>ardea catalog grow</c>: the catalogue collects unknown products, maps what rules can map and, with a provider,
    /// asks the model about the most frequent rest (master plan 6.9), now instead of at the next daily run.
    /// </summary>
    public static readonly MaintenanceCommand Grow = new(
        "catalog-grow",
        "catalog grow",
        "Let the catalogue learn from the unknown products of the sources now, e.g. after the first fill",
        async (services, args, cancellationToken) =>
        {
            await services.GetRequiredService<IJobQueue>().EnqueueAsync(new GrowCatalog(), GrowCatalog.Options, cancellationToken);
            await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
            Console.WriteLine("The worker lets the catalogue learn; follow it on the «Sources» page or in the worker log.");
            _ = args;
            return 0;
        });
}
