using System.Diagnostics;
using System.Text.Json;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Npgsql;
using NpgsqlTypes;

namespace Ardea.Platform.Jobs;

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
        if (options.OrganizationId is { } requested && tenant.OrganizationId is { } current && requested != current)
        {
            throw new InvalidOperationException("A unit of work of one organisation cannot enqueue jobs for another.");
        }

        var organization = options.OrganizationId ?? tenant.OrganizationId;

        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        var payload = JsonSerializer.SerializeToDocument(job, JobRegistry.Json);
        var priority = (short)(options.Priority ?? registration.Options.Priority);
        for (var attempt = 1; ; attempt++)
        {
            if (await InsertAsync(connection, TJob.Kind, payload, options.CoalesceKey, runAt, priority, registration.Options, organization, cancellationToken))
            {
                return true;
            }

            if (await HoldWaitingAsync(connection, options.CoalesceKey!, cancellationToken))
            {
                return false;
            }

            if (attempt == 5)
            {
                throw new InvalidOperationException($"Coalescing on '{options.CoalesceKey}' kept racing with claims.");
            }
        }
    }

    private static async Task<bool> InsertAsync(
        NpgsqlConnection connection, string kind, JsonDocument payload, string? coalesceKey, DateTimeOffset runAt, short priority,
        JobKindOptions options, Guid? organization, CancellationToken cancellationToken)
    {
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
        command.Parameters.Add(new NpgsqlParameter { Value = kind });
        command.Parameters.Add(new NpgsqlParameter { Value = payload, NpgsqlDbType = NpgsqlDbType.Jsonb });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)coalesceKey ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Varchar });
        command.Parameters.Add(new NpgsqlParameter { Value = runAt });
        command.Parameters.Add(new NpgsqlParameter { Value = priority });
        command.Parameters.Add(new NpgsqlParameter { Value = options.MaxAttempts });
        command.Parameters.Add(new NpgsqlParameter { Value = (int)Math.Ceiling(options.Timeout.TotalSeconds) });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)organization ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)Activity.Current?.Id ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Varchar });
        command.Parameters.Add(new NpgsqlParameter { Value = Channel });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return reader.GetInt64(0) > 0;
    }

    private static async Task<bool> HoldWaitingAsync(NpgsqlConnection connection, string coalesceKey, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM platform.job WHERE coalesce_key = $1 AND state = 'pending' FOR SHARE", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = coalesceKey, NpgsqlDbType = NpgsqlDbType.Varchar });
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }
}
