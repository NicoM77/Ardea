using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Ardea.Platform.Jobs;

internal sealed record ClaimedJob(Guid Id, Guid LeaseToken, string Kind, JsonDocument Payload, int Attempt, int MaxAttempts, TimeSpan Timeout, Guid? OrganizationId, string? TraceParent);

internal static class JobStore
{
    private const string FinalState = "CASE WHEN j.max_attempts = 1 THEN 'failed' ELSE 'dead' END";

    private const string HasWaitingSuccessor =
        "j.coalesce_key IS NOT NULL AND EXISTS (SELECT 1 FROM platform.job p WHERE p.coalesce_key = j.coalesce_key AND p.state = 'pending')";

    private const string Superseded = "Superseded by a waiting job with the same coalesce key.";

    public static async Task<IReadOnlyList<ClaimedJob>> ClaimAsync(
        NpgsqlDataSource dataSource, string workerId, string kind, int limit, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE platform.job j
            SET state = 'running', lease_owner = $1, lease_until = now() + $2, lease_token = gen_random_uuid(), attempts = j.attempts + 1
            WHERE j.id IN (
                SELECT id FROM platform.job
                WHERE state = 'pending' AND run_at <= now() AND kind = $3
                ORDER BY priority, run_at
                FOR UPDATE SKIP LOCKED
                LIMIT $4)
            RETURNING j.id, j.lease_token, j.kind, j.payload, j.attempts, j.max_attempts, j.timeout_seconds, j.org_id, j.trace_parent
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
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetFieldValue<JsonDocument>(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                TimeSpan.FromSeconds(reader.GetInt32(6)),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return jobs;
    }

    public static async Task<bool> CompleteAsync(NpgsqlConnection connection, ClaimedJob job, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE platform.job SET state = 'succeeded', finished_at = now(), lease_owner = NULL, lease_until = NULL, lease_token = NULL
            WHERE id = $1 AND lease_token = $2 AND state = 'running'
            """, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = job.Id });
        command.Parameters.Add(new NpgsqlParameter { Value = job.LeaseToken });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public static async Task<bool> RenewAsync(NpgsqlDataSource dataSource, ClaimedJob job, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE platform.job SET lease_until = now() + $3 WHERE id = $1 AND lease_token = $2 AND state = 'running'");
        command.Parameters.Add(new NpgsqlParameter { Value = job.Id });
        command.Parameters.Add(new NpgsqlParameter { Value = job.LeaseToken });
        command.Parameters.Add(new NpgsqlParameter { Value = lease });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public static Task FailAsync(NpgsqlDataSource dataSource, ClaimedJob job, string error, TimeSpan backoff, CancellationToken cancellationToken) =>
        RetryOnCoalesceConflictAsync(async () =>
        {
            await using var command = dataSource.CreateCommand(
                $"""
                UPDATE platform.job j SET
                    state = CASE WHEN j.attempts >= j.max_attempts THEN {FinalState} WHEN {HasWaitingSuccessor} THEN 'succeeded' ELSE 'pending' END,
                    finished_at = CASE WHEN j.attempts >= j.max_attempts OR {HasWaitingSuccessor} THEN now() END,
                    run_at = CASE WHEN j.attempts >= j.max_attempts THEN j.run_at ELSE now() + $4 END,
                    last_error = CASE WHEN j.attempts < j.max_attempts AND {HasWaitingSuccessor} THEN $3 || ' {Superseded}' ELSE $3 END,
                    lease_owner = NULL, lease_until = NULL, lease_token = NULL
                WHERE j.id = $1 AND j.lease_token = $2 AND j.state = 'running'
                """);
            command.Parameters.Add(new NpgsqlParameter { Value = job.Id });
            command.Parameters.Add(new NpgsqlParameter { Value = job.LeaseToken });
            command.Parameters.Add(new NpgsqlParameter { Value = Truncate(error), NpgsqlDbType = NpgsqlDbType.Text });
            command.Parameters.Add(new NpgsqlParameter { Value = backoff });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);

    public static Task ReleaseAsync(NpgsqlDataSource dataSource, ClaimedJob job, CancellationToken cancellationToken) =>
        RetryOnCoalesceConflictAsync(async () =>
        {
            await using var command = dataSource.CreateCommand(
                $"""
                UPDATE platform.job j SET
                    state = CASE WHEN {HasWaitingSuccessor} THEN 'succeeded' ELSE 'pending' END,
                    finished_at = CASE WHEN {HasWaitingSuccessor} THEN now() END,
                    last_error = CASE WHEN {HasWaitingSuccessor} THEN '{Superseded}' ELSE j.last_error END,
                    attempts = greatest(j.attempts - 1, 0), lease_owner = NULL, lease_until = NULL, lease_token = NULL, run_at = now()
                WHERE j.id = $1 AND j.lease_token = $2 AND j.state = 'running'
                """);
            command.Parameters.Add(new NpgsqlParameter { Value = job.Id });
            command.Parameters.Add(new NpgsqlParameter { Value = job.LeaseToken });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);

    public static async Task<int> ReclaimExpiredAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        var reclaimed = 0;
        await RetryOnCoalesceConflictAsync(async () =>
        {
            await using var command = dataSource.CreateCommand(
                $"""
                UPDATE platform.job j SET
                    state = CASE WHEN j.attempts >= j.max_attempts THEN {FinalState} WHEN {HasWaitingSuccessor} THEN 'succeeded' ELSE 'pending' END,
                    finished_at = CASE WHEN j.attempts >= j.max_attempts OR {HasWaitingSuccessor} THEN now() END,
                    last_error = 'Lease expired: the worker stopped while running the job.'
                        || CASE WHEN j.attempts < j.max_attempts AND {HasWaitingSuccessor} THEN ' {Superseded}' ELSE '' END,
                    lease_owner = NULL, lease_until = NULL, lease_token = NULL, run_at = now()
                WHERE j.state = 'running' AND j.lease_until < now()
                """);
            reclaimed = await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
        return reclaimed;
    }

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

    private static async Task RetryOnCoalesceConflictAsync(Func<Task> statement, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await statement();
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken);
            }
        }
    }

    private static string Truncate(string error) => error.Length <= 4000 ? error : error[..4000];
}
