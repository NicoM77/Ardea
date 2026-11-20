using Ardea.Platform.Hosting;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tests.Persistence;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Ardea.Platform.Tests.Jobs;

internal sealed class JobTestHost : IAsyncDisposable
{
    private readonly List<JobProcessor> _workers = [];

    private JobTestHost(MigrationDatabase database, ServiceProvider services)
    {
        Database = database;
        Services = services;
    }

    public const int TestTimeout = 120_000;

    public MigrationDatabase Database { get; }

    public ServiceProvider Services { get; }

    public NpgsqlDataSource DataSource => Services.GetRequiredService<NpgsqlDataSource>();

    public static async Task<JobTestHost> CreateAsync(PostgresServer server, Action<IServiceCollection> configure)
    {
        var database = await MigrationDatabase.CreateAsync(server);
        await using (var owner = await database.OpenOwnerAsync())
        {
            await database.Migrator().MigrateAsync(owner, CancellationToken.None);
            await using var tables = new NpgsqlCommand(
                """
                CREATE TABLE public.test_effect (job_ref text NOT NULL, at timestamptz NOT NULL DEFAULT now());
                CREATE TABLE public.test_business (name text NOT NULL);
                GRANT SELECT, INSERT ON public.test_effect, public.test_business TO ardea_app;
                """, owner);
            await tables.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(database.Options);
        services.AddSingleton(_ => NpgsqlDataSource.Create(database.Options.AppConnectionString("job-tests")));
        services.AddUnitOfWork();
        services.AddJobQueue();
        configure(services);
        return new JobTestHost(database, services.BuildServiceProvider(validateScopes: true));
    }

    public JobProcessor StartWorker(string id, JobWorkerOptions options)
    {
        var worker = new JobProcessor(
            DataSource,
            Services.GetRequiredService<IServiceScopeFactory>(),
            Services.GetRequiredService<JobRegistry>(),
            new WorkerIdentity(id),
            options,
            NullLogger<JobProcessor>.Instance);
        _workers.Add(worker);
        _ = worker.StartAsync(CancellationToken.None);
        return worker;
    }

    public async Task InUnitOfWorkAsync(Func<IServiceProvider, Task> work, bool commit = true)
    {
        await using var scope = Services.CreateAsyncScope();
        await work(scope.ServiceProvider);
        if (commit)
        {
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
        }
    }

    public async Task<long> CountAsync(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<long> CountAsOwnerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.Options.OwnerConnectionString("tests"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    public async Task WaitForAsync(string sql, long expected, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        long last = -1;
        while (DateTime.UtcNow < deadline)
        {
            last = await CountAsync(sql);
            if (last >= expected)
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }

        Assert.Fail($"Timed out after {timeout}: `{sql}` reached {last}, expected {expected}.");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var worker in _workers)
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }

        await Services.DisposeAsync();
    }
}

internal sealed record EffectJob(string Ref) : IJob
{
    public static string Kind => "test.effect";
}

internal sealed class EffectJobHandler(IUnitOfWork unitOfWork) : IJobHandler<EffectJob>
{
    public async Task HandleAsync(EffectJob job, JobContext context, CancellationToken cancellationToken)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("INSERT INTO public.test_effect (job_ref) VALUES ($1)", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = job.Ref });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

internal sealed record GatedJob(string Ref) : IJob
{
    public static string Kind => "test.gated";
}

internal sealed class Gate
{
    public TaskCompletionSource Open { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class GatedJobHandler(IUnitOfWork unitOfWork, Gate gate) : IJobHandler<GatedJob>
{
    public async Task HandleAsync(GatedJob job, JobContext context, CancellationToken cancellationToken)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("INSERT INTO public.test_effect (job_ref) VALUES ($1)", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = job.Ref });
        await command.ExecuteNonQueryAsync(cancellationToken);
        await gate.Open.Task.WaitAsync(cancellationToken);
    }
}

internal sealed record FailingJob(string Ref) : IJob
{
    public static string Kind => "test.failing";
}

internal sealed class FailingJobHandler : IJobHandler<FailingJob>
{
    public Task HandleAsync(FailingJob job, JobContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"{job.Ref} always fails");
}

internal sealed record SingleShotJob(string Ref) : IJob
{
    public static string Kind => "test.single-shot";
}

internal sealed class SingleShotJobHandler : IJobHandler<SingleShotJob>
{
    public Task HandleAsync(SingleShotJob job, JobContext context, CancellationToken cancellationToken) =>
        throw new HttpRequestException("source unavailable");
}

internal sealed record HangingJob(string Ref) : IJob
{
    public static string Kind => "test.hanging";
}

internal sealed class HangingJobHandler : IJobHandler<HangingJob>
{
    public Task HandleAsync(HangingJob job, JobContext context, CancellationToken cancellationToken) =>
        Task.Delay(Timeout.Infinite, cancellationToken);
}

internal sealed record SelfCommittingJob(string Ref) : IJob
{
    public static string Kind => "test.self-committing";
}

internal sealed class SelfCommittingJobHandler(IUnitOfWork unitOfWork) : IJobHandler<SelfCommittingJob>
{
    public async Task HandleAsync(SelfCommittingJob job, JobContext context, CancellationToken cancellationToken)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("INSERT INTO public.test_effect (job_ref) VALUES ($1)", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = job.Ref });
        await command.ExecuteNonQueryAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
