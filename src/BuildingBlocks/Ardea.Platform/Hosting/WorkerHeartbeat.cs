using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ardea.Platform.Hosting;

/// <summary>
/// Writes the worker's heartbeat every 30 seconds; <c>ardea healthcheck worker</c> considers the worker
/// healthy while the heartbeat is younger than two minutes (master plan 12.3).
/// </summary>
public sealed partial class WorkerHeartbeat(NpgsqlDataSource dataSource, WorkerIdentity worker, TimeProvider time, ILogger<WorkerHeartbeat> logger, string version)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startedAt = time.GetUtcNow();
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await BeatAsync(startedAt, stoppingToken);
            }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
            {
                LogBeatFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task BeatAsync(DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO platform.worker_heartbeat (worker_id, started_at, beat_at, version)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (worker_id) DO UPDATE SET started_at = excluded.started_at, beat_at = excluded.beat_at, version = excluded.version
            """);
        command.Parameters.Add(new NpgsqlParameter { Value = worker.Id });
        command.Parameters.Add(new NpgsqlParameter { Value = startedAt });
        command.Parameters.Add(new NpgsqlParameter { Value = time.GetUtcNow() });
        command.Parameters.Add(new NpgsqlParameter { Value = version });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<bool> IsAliveAsync(NpgsqlDataSource dataSource, string workerId, TimeProvider time, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT beat_at FROM platform.worker_heartbeat WHERE worker_id = $1");
        command.Parameters.Add(new NpgsqlParameter { Value = workerId });
        var beatAt = await command.ExecuteScalarAsync(cancellationToken);
        return beatAt is DateTime utc && time.GetUtcNow() - new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)) < MaxAge;
    }

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "Worker heartbeat could not be written")]
    private partial void LogBeatFailed(Exception exception);
}
