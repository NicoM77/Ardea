using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ardea.Platform.Jobs;

public sealed record ScheduleDefinition(string Key, string JobKind, string Cron, string TimeZone, Func<IJobQueue, CancellationToken, Task> Enqueue)
{
    public CronExpression Expression { get; } = CronExpression.Parse(Cron);

    public TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    public DateTimeOffset NextAfter(DateTimeOffset after) =>
        Expression.GetNextOccurrence(after, Zone)
        ?? throw new InvalidOperationException($"Schedule {Key} ({Cron}) has no future occurrence.");
}

public static class ScheduleServiceCollectionExtensions
{
    public static IServiceCollection AddSchedule<TJob>(this IServiceCollection services, string key, string cron, TJob job, string timeZone = "UTC")
        where TJob : IJob
    {
        services.AddSingleton(new ScheduleDefinition(
            key, TJob.Kind, cron, timeZone,
            (queue, ct) => queue.EnqueueAsync(job, new EnqueueOptions { CoalesceKey = $"schedule:{key}" }, ct)));
        return services;
    }
}

internal sealed partial class ScheduleLeader(
    NpgsqlDataSource dataSource,
    IServiceScopeFactory scopes,
    IEnumerable<ScheduleDefinition> definitions,
    TimeProvider time,
    ILogger<ScheduleLeader> logger) : BackgroundService
{
    private const long LockKey = 0x41524445_41534348;
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyList<ScheduleDefinition> _definitions = [.. definitions];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_definitions.Count == 0)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var leader = await dataSource.OpenConnectionAsync(stoppingToken);
                if (await TryLeadAsync(leader, stoppingToken))
                {
                    LogLeading();
                    await SyncDefinitionsAsync(stoppingToken);
                    while (!stoppingToken.IsCancellationRequested && await IsAliveAsync(leader, stoppingToken))
                    {
                        await RunDueAsync(stoppingToken);
                        await Task.Delay(Interval, stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private static async Task<bool> IsAliveAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var probe = new NpgsqlCommand("SELECT 1", connection);
            await probe.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }

    private static async Task<bool> TryLeadAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"SELECT pg_try_advisory_lock({LockKey})", connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    internal async Task SyncDefinitionsAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        foreach (var definition in _definitions)
        {
            await using var command = dataSource.CreateCommand(
                """
                INSERT INTO platform.schedule (key, job_kind, cron, time_zone, enabled, next_run_at)
                VALUES ($1, $2, $3, $4, true, $5)
                ON CONFLICT (key) DO UPDATE SET job_kind = excluded.job_kind, cron = excluded.cron, time_zone = excluded.time_zone,
                    next_run_at = CASE WHEN platform.schedule.cron <> excluded.cron OR platform.schedule.time_zone <> excluded.time_zone
                        THEN excluded.next_run_at ELSE platform.schedule.next_run_at END
                """);
            command.Parameters.Add(new NpgsqlParameter { Value = definition.Key });
            command.Parameters.Add(new NpgsqlParameter { Value = definition.JobKind });
            command.Parameters.Add(new NpgsqlParameter { Value = definition.Cron });
            command.Parameters.Add(new NpgsqlParameter { Value = definition.TimeZone });
            command.Parameters.Add(new NpgsqlParameter { Value = definition.NextAfter(now) });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal async Task RunDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        foreach (var definition in _definitions)
        {
            try
            {
                await RunIfDueAsync(definition, now, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogScheduleFailed(ex, definition.Key);
            }
        }
    }

    private async Task RunIfDueAsync(ScheduleDefinition definition, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<Persistence.IUnitOfWork>();
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using (var due = new NpgsqlCommand(
            "SELECT 1 FROM platform.schedule WHERE key = $1 AND enabled AND next_run_at <= $2 FOR UPDATE SKIP LOCKED", connection))
        {
            due.Parameters.Add(new NpgsqlParameter { Value = definition.Key });
            due.Parameters.Add(new NpgsqlParameter { Value = now });
            if (await due.ExecuteScalarAsync(cancellationToken) is null)
            {
                return;
            }
        }

        await definition.Enqueue(scope.ServiceProvider.GetRequiredService<IJobQueue>(), cancellationToken);
        await using (var next = new NpgsqlCommand(
            "UPDATE platform.schedule SET last_run_at = $2, next_run_at = $3 WHERE key = $1", connection))
        {
            next.Parameters.Add(new NpgsqlParameter { Value = definition.Key });
            next.Parameters.Add(new NpgsqlParameter { Value = now });
            next.Parameters.Add(new NpgsqlParameter { Value = definition.NextAfter(now) });
            await next.ExecuteNonQueryAsync(cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        LogEnqueued(definition.Key);
    }

    [LoggerMessage(EventId = 2101, Level = LogLevel.Information, Message = "This worker now runs the schedules")]
    private partial void LogLeading();

    [LoggerMessage(EventId = 2102, Level = LogLevel.Debug, Message = "Schedule {Key} enqueued its run")]
    private partial void LogEnqueued(string key);

    [LoggerMessage(EventId = 2104, Level = LogLevel.Error, Message = "Schedule {Key} could not enqueue its run")]
    private partial void LogScheduleFailed(Exception exception, string key);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Warning, Message = "Running schedules failed")]
    private partial void LogFailed(Exception exception);
}
