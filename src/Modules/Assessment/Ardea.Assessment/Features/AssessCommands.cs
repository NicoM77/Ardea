using Ardea.Assessment.Assessing;
using Ardea.Assessment.Persistence;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assessment.Features;

internal static class AssessCommands
{
    public static readonly MaintenanceCommand Recompute = new(
        "assess-recompute",
        "assess recompute",
        "Assess every alert again with the current rule set, e.g. after a new rule set version",
        async (services, args, cancellationToken) =>
        {
            var db = services.GetRequiredService<AssessDbContext>();
            var jobs = services.GetRequiredService<IJobQueue>();
            var alerts = await db.Database
                .SqlQuery<AlertKey>($"""SELECT organization_id, entry_id FROM assess.alerts_to_reassess()""")
                .ToListAsync(cancellationToken);
            foreach (var alert in alerts)
            {
                await jobs.EnqueueAsync(new AssessAlert(alert.EntryId), AssessAlert.Options(alert.OrganizationId, alert.EntryId), cancellationToken);
            }

            await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
            Console.WriteLine($"{alerts.Count} alert(s) in {alerts.Select(a => a.OrganizationId).Distinct().Count()} organisation(s) will be assessed again by the worker.");
            _ = args;
            return 0;
        });

    private sealed record AlertKey(Guid OrganizationId, Guid EntryId);
}
