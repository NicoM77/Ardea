using Ardea.Platform.Persistence;
using Npgsql;

namespace Ardea.Platform.Jobs;

public sealed record AbandonedJob(Guid Id, string Kind, string State, int Attempts, int MaxAttempts, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);

public sealed record JobCounts(long Pending, long Running, long Dead, long Failed);

public interface IJobAdministration
{
    Task<IReadOnlyList<AbandonedJob>> ListAbandonedAsync(int limit, CancellationToken cancellationToken);

    Task<JobCounts> CountAsync(CancellationToken cancellationToken);

    Task<bool> RetryAsync(Guid jobId, CancellationToken cancellationToken);
}

internal sealed class JobAdministration(IUnitOfWork unitOfWork) : IJobAdministration
{
    public async Task<IReadOnlyList<AbandonedJob>> ListAbandonedAsync(int limit, CancellationToken cancellationToken)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, kind, state, attempts, max_attempts, last_error, created_at, finished_at
            FROM platform.job WHERE state IN ('dead', 'failed')
            ORDER BY finished_at DESC NULLS LAST LIMIT $1
            """, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = Math.Clamp(limit, 1, 500) });
        var jobs = new List<AbandonedJob>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(new AbandonedJob(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return jobs;
    }

    public async Task<JobCounts> CountAsync(CancellationToken cancellationToken)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FILTER (WHERE state = 'pending'), count(*) FILTER (WHERE state = 'running'),
                   count(*) FILTER (WHERE state = 'dead'), count(*) FILTER (WHERE state = 'failed')
            FROM platform.job
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new JobCounts(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    public async Task<bool> RetryAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE platform.job SET state = 'pending', attempts = 0, run_at = now(), finished_at = NULL
            WHERE id = $1 AND state IN ('dead', 'failed')
              AND NOT (coalesce_key IS NOT NULL AND EXISTS (
                  SELECT 1 FROM platform.job p WHERE p.coalesce_key = platform.job.coalesce_key AND p.state = 'pending'))
            RETURNING pg_notify('{JobQueue.Channel}', kind)
            """, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = jobId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }
}
