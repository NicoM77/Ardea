using System.Diagnostics;
using System.Text.Json;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Npgsql;
using NpgsqlTypes;

namespace Ardea.Platform.Jobs;

/// <summary>
/// Writes jobs into <c>platform.job</c> on the unit of work's connection and transaction. The table
/// is the outbox at the same time: there is no window in which a change exists without its
/// follow-up job (master plan 3.4). <c>NOTIFY</c> wakes the workers when the transaction commits.
/// </summary>
internal sealed class JobQueue(IUnitOfWork unitOfWork, JobRegistry registry, ITenantContext tenant, TimeProvider time) : IJobQueue
{
    public const string Channel = "ardea_jobs";

    public async Task<bool> EnqueueAsync<TJob>(TJob job, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob
    {
        if (!registry.TryGet(TJob.Kind, out var registration))
        {
            throw new InvalidOperationException($"Job kind '{TJob.Kind}' is not registered.");
        }

        options ??= new EnqueueOptions();
        var now = time.GetUtcNow();
        var runAt = options.RunAt ?? now + (options.Delay ?? TimeSpan.Zero);
        var organization = options.OrganizationId ?? tenant.OrganizationId;

        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            WITH inserted AS (
                INSERT INTO platform.job (kind, payload, coalesce_key, run_at, priority, state, attempts, max_attempts, timeout_seconds, org_id, trace_parent)
                VALUES ($1, $2, $3, $4, $5, 'pending', 0, $6, $7, $8, $9)
                ON CONFLICT (coalesce_key) WHERE state = 'pending' AND coalesce_key IS NOT NULL DO NOTHING
                RETURNING id
            )
            SELECT count(*), pg_notify($10, $1) FROM inserted
            """, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = TJob.Kind });
        command.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.SerializeToDocument(job, JobRegistry.Json), NpgsqlDbType = NpgsqlDbType.Jsonb });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)options.CoalesceKey ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Varchar });
        command.Parameters.Add(new NpgsqlParameter { Value = runAt });
        command.Parameters.Add(new NpgsqlParameter { Value = (short)(options.Priority ?? registration.Options.Priority) });
        command.Parameters.Add(new NpgsqlParameter { Value = registration.Options.MaxAttempts });
        command.Parameters.Add(new NpgsqlParameter { Value = (int)Math.Ceiling(registration.Options.Timeout.TotalSeconds) });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)organization ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)Activity.Current?.Id ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Varchar });
        command.Parameters.Add(new NpgsqlParameter { Value = Channel });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return reader.GetInt64(0) > 0;
    }
}
