using System.Runtime.CompilerServices;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sync;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Ardea.Intel.Tests;

/// <summary>Platform and Intel schemas on a fresh database, with a scripted provider.</summary>
internal sealed class IntelTestHost : IAsyncDisposable
{
    private IntelTestHost(ServiceProvider services, DatabaseOptions database, ScriptedProvider provider, FakeTimeProvider clock)
    {
        Services = services;
        Database = database;
        Provider = provider;
        Clock = clock;
    }

    public ServiceProvider Services { get; }

    public DatabaseOptions Database { get; }

    public ScriptedProvider Provider { get; }

    public FakeTimeProvider Clock { get; }

    public static async Task<IntelTestHost> StartAsync(PostgresServer server)
    {
        var name = await server.CreateDatabaseAsync("intel");
        var database = new DatabaseOptions
        {
            Host = server.Host,
            Port = server.Port,
            Database = name,
            OwnerUser = PostgresServer.OwnerUser,
            OwnerPassword = PostgresServer.OwnerPassword,
            AppPassword = "app-test-password",
        };
        await using (var owner = new NpgsqlConnection(database.OwnerConnectionString("tests")))
        {
            await owner.OpenAsync();
            await new DatabaseMigrator(database, [PlatformDatabase.Instance, new IntelModule().Database!], new NoBackup(), NullLogger<DatabaseMigrator>.Instance)
                .MigrateAsync(owner, CancellationToken.None);
        }

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var provider = new ScriptedProvider();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SCRATCH_DIR"] = Path.Combine(Path.GetTempPath(), "ardea-intel-tests"),
        }).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(database);
        services.AddSingleton(_ => NpgsqlDataSource.Create(database.AppConnectionString("intel-tests")));
        services.AddUnitOfWork();
        services.AddJobQueue();
        services.AddSingleton<ISourceProvider>(provider);
        new IntelModule().AddServices(services, configuration);
        new IntelModule().AddWorkerServices(services, configuration);
        var built = services.BuildServiceProvider(validateScopes: true);
        await built.GetRequiredService<IEnumerable<Microsoft.Extensions.Hosting.IHostedService>>().OfType<SourceRegistrar>().Single().StartAsync(CancellationToken.None);
        return new IntelTestHost(built, database, provider, clock);
    }

    /// <summary>Runs one sync of the scripted source the way the worker does, and commits the job's unit of work.</summary>
    public async Task SyncAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<SyncSourceHandler>();
        await handler.HandleAsync(new SyncSource(ScriptedProvider.Key), new JobContext(Guid.CreateVersion7(), SyncSource.Kind, 1, null), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.OwnerConnectionString("tests"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.OwnerConnectionString("tests"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Runs the normalisation job the way the worker does.</summary>
    public async Task NormalizeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NormalizeRecordsHandler>()
            .HandleAsync(new NormalizeRecords(ScriptedProvider.Key), new JobContext(Guid.CreateVersion7(), NormalizeRecords.Kind, 1, null), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync() => Services.DisposeAsync();

    private sealed class NoBackup : IBackupWriter
    {
        public Task<string?> WriteAsync(NpgsqlConnection owner, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}

/// <summary>A provider that yields what the test scripts, and can fail after a number of items.</summary>
internal sealed class ScriptedProvider : ISourceProvider
{
    public const string Key = "scripted";

    public SourceDescriptor Descriptor { get; } = new()
    {
        Key = Key,
        Name = "Scripted test source",
        Kind = SourceKind.Record,
        Operator = "Tests",
        OperatorKind = OperatorKind.Community,
        DefaultInterval = TimeSpan.FromHours(1),
        AllowedHosts = ["example.org"],
        Attribution = "Test data",
    };

    public List<FetchItem> Items { get; } = [];

    public int? FailAfter { get; set; }

    public string? CursorSeen { get; private set; }

    public SyncMode? ModeSeen { get; private set; }

    public async IAsyncEnumerable<FetchItem> FetchAsync(SyncContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CursorSeen = context.Cursor;
        ModeSeen = context.Mode;
        var yielded = 0;
        foreach (var item in Items)
        {
            if (FailAfter is { } limit && yielded >= limit)
            {
                throw new HttpRequestException("source went away");
            }

            await Task.Yield();
            yielded++;
            yield return item;
        }
    }

    /// <summary><c>{"id":"X","bad":true}</c> goes to quarantine; anything else becomes a vulnerability observation with alias TEST:id.</summary>
    public NormalizationResult Normalize(RawRecord record)
    {
        using var document = System.Text.Json.JsonDocument.Parse(record.Content);
        if (document.RootElement.TryGetProperty("bad", out _))
        {
            return NormalizationResult.Quarantine("scripted bad record");
        }

        return NormalizationResult.Success(new Observation
        {
            Kind = ObservationKind.Vulnerability,
            Title = document.RootElement.TryGetProperty("title", out var title) ? title.GetString() : null,
            Claims = [new ClaimBlock { Aliases = [new Identifier("TEST", record.ExternalId)] }],
        });
    }

    public static RawRecord Json(string id, string json) => new(id, json, "application/json");
}
