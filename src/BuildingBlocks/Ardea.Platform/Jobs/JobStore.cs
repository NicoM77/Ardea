using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Ardea.Platform.Jobs;

/// <summary>A job as claimed by a worker.</summary>
internal sealed record ClaimedJob(Guid Id, string Kind, JsonDocument Payload, int Attempt, int MaxAttempts, TimeSpan Timeout, Guid? OrganizationId, string? TraceParent);

/// <summary>
/// The SQL of the job system (master plan 3.4). Claiming, renewing, failing and reclaiming run in
/// their own short transactions; completing runs inside the job's unit of work, so the job's effect
/// and its completion commit together or not at all.
/// </summary>
internal static class JobStore
{
    /// <summary>A terminal state for jobs that never retry: the cause reschedules itself.</summary>
    private const string FinalState = "CASE WHEN max_attempts = 1 THEN 'failed' ELSE 'dead' END";

    public static async Task<IReadOnlyList<ClaimedJob>> ClaimAsync(
        NpgsqlDataSource dataSource, string workerId, string kind, int limit, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE platform.job j
            SET state = 'running', lease_owner = $1, lease_until = now() + $2, attempts = j.attempts + 1
            WHERE j.id IN (
                SELECT id FROM platform.job
                WHERE state = 'pending' AND run_at <= now() AND kind = $3
                ORDER BY priority, run_at
                FOR UPDATE SKIP LOCKED
                LIMIT $4)
            RETURNING j.id, j.kind, j.payload, j.attempts, j.max_attempts, j.timeout_seconds, j.org_id, j.trace_parent
            """);
        command.Parameters.Add(new NpgsqlParameter { Value = workerId });
        command.Parameters.Add(new NpgsqlParameter { Value = lease });
        command.Parameters.Add(new NpgsqlParameter { Value = kind });
        command.Parameters.Add(new NpgsqlParameter { Value = limit });

        var jobs = new List<ClaimedJob>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(new ClaimedJob(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetFieldValue<JsonDocument>(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                TimeSpan.FromSeconds(reader.GetInt32(5)),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return jobs;
    }

    /// <summary>Marks the job done inside the job's transaction; <c>false</c> when this worker no longer holds it.</summary>
    public static async Task<bool> CompleteAsync(NpgsqlConnection connection, Guid jobId, string workerId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE platform.job SET state = 'succeeded', finished_at = now(), lease_owner = NULL, lease_until = NULL
            WHERE id = $1 AND lease_owner = $2 AND state = 'running'
            """, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = jobId });
        command.Parameters.Add(new NpgsqlParameter { Value = workerId });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Extends the lease while the worker is alive; <c>false</c> when the lease was lost.</summary>
    public static async Task<bool> RenewAsync(NpgsqlDataSource dataSource, Guid jobId, string workerId, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE platform.job SET lease_until = now() + $3 WHERE id = $1 AND lease_owner = $2 AND state = 'running'");
        command.Parameters.Add(new NpgsqlParameter { Value = jobId });
        command.Parameters.Add(new NpgsqlParameter { Value = workerId });
        command.Parameters.Add(new NpgsqlParameter { Value = lease });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Records a failed attempt: back to pending after the back-off, or terminal after the last
    /// attempt. A job whose coalesce key already has a waiting successor is superseded by it.
    /// </summary>
    public static async Task FailAsync(NpgsqlDataSource dataSource, Guid jobId, string workerId, string error, TimeSpan backoff, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            UPDATE platform.job j SET
                state = CASE
                    WHEN j.attempts >= j.max_attempts THEN {FinalState}
                    WHEN j.coalesce_key IS NOT NULL AND EXISTS (
                        SELECT 1 FROM platform.job p WHERE p.coalesce_key = j.coalesce_key AND p.state = 'pending') THEN 'succeeded'
                    ELSE 'pending' END,
                run_at = CASE WHEN j.attempts >= j.max_attempts THEN j.run_at ELSE now() + $4 END,
                finished_at = CASE WHEN j.attempts >= j.max_attempts THEN now() END,
                last_error = $3, lease_owner = NULL, lease_until = NULL
            WHERE j.id = $1 AND j.lease_owner = $2 AND j.state = 'running'
            """);
        command.Parameters.Add(new NpgsqlParameter { Value = jobId });
        command.Parameters.Add(new NpgsqlParameter { Value = workerId });
        command.Parameters.Add(new NpgsqlParameter { Value = Truncate(error), NpgsqlDbType = NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter { Value = backoff });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Gives a job back without counting the attempt, e.g. on an orderly shutdown.</summary>
    public static async Task ReleaseAsync(NpgsqlDataSource dataSource, Guid jobId, string workerId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE platform.job j SET
                state = CASE WHEN j.coalesce_key IS NOT NULL AND EXISTS (
                    SELECT 1 FROM platform.job p WHERE p.coalesce_key = j.coalesce_key AND p.state = 'pending') THEN 'succeeded' ELSE 'pending' END,
                attempts = greatest(j.attempts - 1, 0), lease_owner = NULL, lease_until = NULL, run_at = now()
            WHERE j.id = $1 AND j.lease_owner = $2 AND j.state = 'running'
            """);
        command.Parameters.Add(new NpgsqlParameter { Value = jobId });
        command.Parameters.Add(new NpgsqlParameter { Value = workerId });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The watchdog: jobs whose lease ran out (their worker died) run again; the attempt counts.</summary>
    public static async Task<int> ReclaimExpiredAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            UPDATE platform.job j SET
                state = CASE
                    WHEN j.attempts >= j.max_attempts THEN {FinalState}
                    WHEN j.coalesce_key IS NOT NULL AND EXISTS (
                        SELECT 1 FROM platform.job p WHERE p.coalesce_key = j.coalesce_key AND p.state = 'pending') THEN 'succeeded'
                    ELSE 'pending' END,
                finished_at = CASE WHEN j.attempts >= j.max_attempts THEN now() END,
                last_error = 'Lease expired: the worker stopped while running the job.',
                lease_owner = NULL, lease_until = NULL, run_at = now()
            WHERE j.state = 'running' AND j.lease_until < now()
            """);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Removes finished jobs: succeeded after 7 days, given up after 30 (master plan 3.4).</summary>
    public static async Task<int> CleanUpAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            DELETE FROM platform.job
            WHERE (state = 'succeeded' AND finished_at < now() - interval '7 days')
               OR (state IN ('dead', 'failed') AND finished_at < now() - interval '30 days')
            """, connection);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Truncate(string error) => error.Length <= 4000 ? error : error[..4000];
}
