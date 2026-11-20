using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Ardea.Platform.Hosting;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ardea.Platform.Jobs;

public sealed record JobWorkerOptions
{
    public int Concurrency { get; init; } = 8;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan Lease { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan WatchdogInterval { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan BackoffBase { get; init; } = Backoff.Base;

    public TimeSpan ShutdownGrace { get; init; } = TimeSpan.FromSeconds(20);
}

internal sealed partial class JobProcessor(
    NpgsqlDataSource dataSource,
    IServiceScopeFactory scopes,
    JobRegistry registry,
    WorkerIdentity worker,
    JobWorkerOptions options,
    ILogger<JobProcessor> logger) : BackgroundService
{
    internal static readonly ActivitySource Activity = new("Ardea.Jobs");

    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly ConcurrentDictionary<Guid, Task> _running = new();
    private readonly ConcurrentDictionary<string, int> _runningByKind = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _crash = new();

    private readonly CancellationTokenSource _hardStop = new();
    private CancellationToken _stopping;
    private int _runningTotal;

    internal void SimulateCrash() => _crash.Cancel();

    internal int RunningCount => _runningTotal;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _crash.Token);
        _stopping = linked.Token;
        var listener = ListenAsync(_stopping);
        var watchdog = WatchdogAsync(_stopping);

        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                var claimed = false;
                try
                {
                    claimed = await ClaimAndStartAsync(_stopping);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogClaimFailed(ex);
                }

                if (!claimed || Volatile.Read(ref _runningTotal) >= options.Concurrency)
                {
                    await WaitForWorkAsync(_stopping);
                }
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }

        await DrainAsync();
        await Task.WhenAll(Quiet(listener), Quiet(watchdog));
    }

    private async Task<bool> ClaimAndStartAsync(CancellationToken cancellationToken)
    {
        var claimedAny = false;
        foreach (var kind in registry.Kinds.OrderBy(k => k.Options.Priority))
        {
            var free = Math.Min(
                options.Concurrency - Volatile.Read(ref _runningTotal),
                kind.Options.MaxConcurrency - _runningByKind.GetValueOrDefault(kind.Kind));
            if (free <= 0)
            {
                continue;
            }

            var jobs = await JobStore.ClaimAsync(dataSource, worker.Id, kind.Kind, free, options.Lease, cancellationToken);
            foreach (var job in jobs)
            {
                Start(job, kind);
            }

            claimedAny |= jobs.Count > 0;
        }

        return claimedAny;
    }

    private void Start(ClaimedJob job, JobRegistry.Registration kind)
    {
        Interlocked.Increment(ref _runningTotal);
        _runningByKind.AddOrUpdate(kind.Kind, 1, (_, n) => n + 1);
        var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _running[job.Id] = Task.Run(async () =>
        {
            await registered.Task;
            try
            {
                await RunAsync(job, kind);
            }
            finally
            {
                _runningByKind.AddOrUpdate(kind.Kind, 0, (_, n) => n - 1);
                Interlocked.Decrement(ref _runningTotal);
                _running.TryRemove(job.Id, out _);
                _wake.Writer.TryWrite(true);
            }
        });
        registered.SetResult();
    }

    private async Task RunAsync(ClaimedJob job, JobRegistry.Registration kind)
    {
        using var timeout = new CancellationTokenSource(job.Timeout);
        using var leaseLost = new CancellationTokenSource();
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(_hardStop.Token, _crash.Token, timeout.Token, leaseLost.Token);
        using var renewal = new CancellationTokenSource();
        var renewing = RenewAsync(job, leaseLost, renewal.Token);

        using var activity = Activity.StartActivity($"job {job.Kind}", ActivityKind.Consumer, job.TraceParent);
        activity?.SetTag("ardea.job.id", job.Id);
        activity?.SetTag("ardea.job.attempt", job.Attempt);

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<Audit.AuditOrigin>().Origin = $"job:{job.Kind}";
            if (job.OrganizationId is { } organization)
            {
                scope.ServiceProvider.GetRequiredService<TenantContext>().SetOrganization(organization);
            }

            var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
            unitOfWork.ReserveCommitForOwner();

            var context = new JobContext(job.Id, job.Kind, job.Attempt, job.OrganizationId);
            await kind.Invoke(scope.ServiceProvider, job.Payload, context, execution.Token);

            await renewal.CancelAsync();
            await Quiet(renewing);
            var connection = await unitOfWork.GetConnectionAsync(execution.Token);
            if (!await JobStore.CompleteAsync(connection, job, execution.Token))
            {
                LogLeaseLost(job.Id, job.Kind);
                return;
            }

            await unitOfWork.CommitAsOwnerAsync(execution.Token);
            LogSucceeded(job.Id, job.Kind, job.Attempt);
        }
        catch (Exception) when (_crash.IsCancellationRequested)
        {
        }
        catch (Exception) when (leaseLost.IsCancellationRequested)
        {
            LogLeaseLost(job.Id, job.Kind);
        }
        catch (OperationCanceledException) when (_hardStop.IsCancellationRequested && !timeout.IsCancellationRequested)
        {
            await Settle(() => JobStore.ReleaseAsync(dataSource, job, CancellationToken.None), job);
        }
        catch (Exception ex)
        {
            var error = timeout.IsCancellationRequested ? $"Timed out after {job.Timeout}." : $"{ex.GetType().Name}: {ex.Message}";
            activity?.SetStatus(ActivityStatusCode.Error, error);
            LogFailed(ex, job.Id, job.Kind, job.Attempt, job.MaxAttempts);
            await Settle(
                () => JobStore.FailAsync(dataSource, job, error, Backoff.After(job.Attempt, Random.Shared.NextDouble(), options.BackoffBase), CancellationToken.None),
                job);
        }
        finally
        {
            await renewal.CancelAsync();
            await Quiet(renewing);
        }
    }

    private async Task Settle(Func<Task> record, ClaimedJob job)
    {
        try
        {
            await record();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSettleFailed(ex, job.Id, job.Kind);
        }
    }

    private async Task RenewAsync(ClaimedJob job, CancellationTokenSource leaseLost, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _crash.Token);
        var interval = options.Lease / 3;
        while (!linked.IsCancellationRequested)
        {
            await Task.Delay(interval, linked.Token);
            try
            {
                if (!await JobStore.RenewAsync(dataSource, job, options.Lease, linked.Token))
                {
                    await leaseLost.CancelAsync();
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRenewFailed(ex, job.Id);
            }
        }
    }

    private async Task WatchdogAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var reclaimed = await JobStore.ReclaimExpiredAsync(dataSource, cancellationToken);
                if (reclaimed > 0)
                {
                    LogReclaimed(reclaimed);
                    _wake.Writer.TryWrite(true);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogWatchdogFailed(ex);
            }

            await Task.Delay(options.WatchdogInterval, cancellationToken);
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
                connection.Notification += (_, _) => _wake.Writer.TryWrite(true);
                await using (var listen = new NpgsqlCommand($"LISTEN {JobQueue.Channel}", connection))
                {
                    await listen.ExecuteNonQueryAsync(cancellationToken);
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    await connection.WaitAsync(cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogListenFailed(ex);
                await Task.Delay(options.PollInterval, cancellationToken);
            }
        }
    }

    private async Task WaitForWorkAsync(CancellationToken cancellationToken)
    {
        using var poll = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        poll.CancelAfter(options.PollInterval);
        try
        {
            await _wake.Reader.WaitToReadAsync(poll.Token);
            _wake.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task DrainAsync()
    {
        var running = _running.Values.ToArray();
        if (running.Length > 0 && !_crash.IsCancellationRequested)
        {
            await Task.WhenAny(Task.WhenAll(running), Task.Delay(options.ShutdownGrace));
        }

        await _hardStop.CancelAsync();
        await Task.WhenAll(running.Select(Quiet));
    }

    private static async Task Quiet(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override void Dispose()
    {
        _crash.Dispose();
        _hardStop.Dispose();
        base.Dispose();
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "Job {JobId} ({Kind}) succeeded on attempt {Attempt}")]
    private partial void LogSucceeded(Guid jobId, string kind, int attempt);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "Job {JobId} ({Kind}) failed on attempt {Attempt} of {MaxAttempts}")]
    private partial void LogFailed(Exception exception, Guid jobId, string kind, int attempt, int maxAttempts);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning, Message = "Job {JobId} ({Kind}) lost its lease; its work was rolled back")]
    private partial void LogLeaseLost(Guid jobId, string kind);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information, Message = "Reclaimed {Count} job(s) from stopped workers")]
    private partial void LogReclaimed(int count);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Warning, Message = "Claiming jobs failed")]
    private partial void LogClaimFailed(Exception exception);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Warning, Message = "Renewing the lease of job {JobId} failed")]
    private partial void LogRenewFailed(Exception exception, Guid jobId);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Warning, Message = "Job watchdog failed")]
    private partial void LogWatchdogFailed(Exception exception);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Error, Message = "Recording the outcome of job {JobId} ({Kind}) failed; the watchdog settles it after the lease")]
    private partial void LogSettleFailed(Exception exception, Guid jobId, string kind);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Warning, Message = "Listening for new jobs failed; polling until reconnected")]
    private partial void LogListenFailed(Exception exception);
}
