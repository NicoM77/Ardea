using Ardea.Platform.Audit;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Npgsql;

namespace Ardea.Host.Commands;

internal static class DemoJobRunner
{
    private static readonly string[] Kinds =
    [
        "assets.profile-changed",
        "intel.normalize-records",
        "intel.correlate-entry",
        "intel.notify-entry-changed",
        "assess.match-project",
        "assess.match-entry",
        "assess.assess-alert",
        "notify.evaluate",
        "notify.distribute-warning",
        "notify.receive-warning",
    ];

    private const int MaxRounds = 200;

    public static async Task<int> RunUntilIdleAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var dataSource = services.GetRequiredService<NpgsqlDataSource>();
        var registry = services.GetRequiredService<JobRegistry>();
        var scopes = services.GetRequiredService<IServiceScopeFactory>();
        var total = 0;
        for (var round = 0; round < MaxRounds; round++)
        {
            await BringForwardAsync(dataSource, cancellationToken);
            var ran = 0;
            foreach (var kind in Kinds)
            {
                foreach (var job in await JobStore.ClaimAsync(dataSource, "seed-demo", kind, 100, TimeSpan.FromMinutes(10), cancellationToken))
                {
                    await RunAsync(job, registry, scopes, dataSource, cancellationToken);
                    ran++;
                }
            }

            if (ran == 0)
            {
                return total;
            }

            total += ran;
        }

        throw new InvalidOperationException("The jobs of the pipeline keep producing new jobs.");
    }

    private static async Task BringForwardAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE platform.job SET run_at = now() WHERE state = 'pending' AND run_at > now() AND kind = ANY($1)");
        command.Parameters.Add(new NpgsqlParameter { Value = Kinds });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RunAsync(ClaimedJob job, JobRegistry registry, IServiceScopeFactory scopes, NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(job.Timeout);
        try
        {
            if (!registry.TryGet(job.Kind, out var registration))
            {
                throw new InvalidOperationException($"No handler for job kind {job.Kind}.");
            }

            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<AuditOrigin>().Origin = DemoSeedContext.AuditOrigin;
            if (job.OrganizationId is { } organization)
            {
                scope.ServiceProvider.GetRequiredService<TenantContext>().SetOrganization(organization);
            }

            var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
            unitOfWork.ReserveCommitForOwner();
            await registration.Invoke(scope.ServiceProvider, job.Payload, new JobContext(job.Id, job.Kind, job.Attempt, job.OrganizationId), timeout.Token);
            var connection = await unitOfWork.GetConnectionAsync(timeout.Token);
            if (!await JobStore.CompleteAsync(connection, job, timeout.Token))
            {
                throw new InvalidOperationException($"The job {job.Id} ({job.Kind}) was taken by another worker.");
            }

            await unitOfWork.CommitAsOwnerAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || timeout.IsCancellationRequested)
        {
            await JobStore.FailAsync(dataSource, job, $"{ex.GetType().Name}: {ex.Message}", TimeSpan.FromMinutes(1), CancellationToken.None);
            throw new InvalidOperationException($"The job {job.Kind} failed: {ex.Message}", ex);
        }
    }
}
