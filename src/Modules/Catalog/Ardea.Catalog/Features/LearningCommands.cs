using Ardea.Catalog.Learning;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Catalog.Features;

internal static class LearningCommands
{
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
