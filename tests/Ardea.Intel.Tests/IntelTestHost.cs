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

internal sealed class IntelTestHost : IAsyncDisposable
{
    private IntelTestHost(ServiceProvider services, DatabaseOptions database, ScriptedProvider provider, ScriptedSignalProvider signals, FakeTimeProvider clock)
    {
        Signals = signals;
        Services = services;
        Database = database;
        Provider = provider;
        Clock = clock;
    }

    public ServiceProvider Services { get; }

    public ScriptedSignalProvider Signals { get; }

    public DatabaseOptions Database { get; }

    public ScriptedProvider Provider { get; }

    public FakeTimeProvider Clock { get; }

    public static async Task<IntelTestHost> StartAsync(PostgresServer server, params ISourceProvider[] sources)
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
            await new DatabaseMigrator(database, [PlatformDatabase.Instance, new Catalog.CatalogModule().Database!, new IntelModule().Database!], new NoBackup(), NullLogger<DatabaseMigrator>.Instance)
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
        foreach (var source in sources)
        {
            services.AddSingleton(source);
        }

        var signals = new ScriptedSignalProvider();
        services.AddSingleton<ISignalProvider>(signals);
        new Catalog.CatalogModule().AddServices(services, configuration);
        new IntelModule().AddServices(services, configuration);
        new IntelModule().AddWorkerServices(services, configuration);
        var built = services.BuildServiceProvider(validateScopes: true);
        await built.GetRequiredService<IEnumerable<Microsoft.Extensions.Hosting.IHostedService>>().OfType<SourceRegistrar>().Single().StartAsync(CancellationToken.None);
        return new IntelTestHost(built, database, provider, signals, clock);
    }

    public async Task SyncAsync(string key = ScriptedProvider.Key)
    {
        await using var scope = Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<SyncSourceHandler>();
        await handler.HandleAsync(new SyncSource(key), new JobContext(Guid.CreateVersion7(), SyncSource.Kind, 1, null), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
    }

    public async Task CheckCompletenessAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CheckCompletenessHandler>()
            .HandleAsync(new CheckCompleteness(), new JobContext(Guid.CreateVersion7(), CheckCompleteness.Kind, 1, null), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
    }

    public async Task<int> RunCommandAsync(Platform.Modules.MaintenanceCommand command, params string[] args)
    {
        await using var scope = Services.CreateAsyncScope();
        return await command.RunAsync(scope.ServiceProvider, args, CancellationToken.None);
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

    public async Task NormalizeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NormalizeRecordsHandler>()
            .HandleAsync(new NormalizeRecords(ScriptedProvider.Key), new JobContext(Guid.CreateVersion7(), NormalizeRecords.Kind, 1, null), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
    }

    public async Task RunJobsAsync(params string[] kinds)
    {
        for (var round = 0; round < 10; round++)
        {
            await using var connection = new NpgsqlConnection(Database.OwnerConnectionString("tests"));
            await connection.OpenAsync();
            await using var select = new NpgsqlCommand("SELECT id, kind, payload::text FROM platform.job WHERE state = 'pending' AND kind = ANY($1) ORDER BY id", connection);
            select.Parameters.Add(new NpgsqlParameter { Value = kinds });
            var due = new List<(Guid Id, string Kind, string Payload)>();
            await using (var reader = await select.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    due.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
                }
            }

            if (due.Count == 0)
            {
                return;
            }

            foreach (var (id, kind, payload) in due)
            {
                await using (var scope = Services.CreateAsyncScope())
                {
                    using var document = System.Text.Json.JsonDocument.Parse(payload);
                    await RunAsync(scope.ServiceProvider, kind, document);
                    await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
                }

                await using var done = new NpgsqlCommand("UPDATE platform.job SET state = 'succeeded', finished_at = now() WHERE id = $1", connection);
                done.Parameters.Add(new NpgsqlParameter { Value = id });
                await done.ExecuteNonQueryAsync();
            }
        }
    }

    private static Task RunAsync(IServiceProvider services, string kind, System.Text.Json.JsonDocument payload)
    {
        var context = new JobContext(Guid.CreateVersion7(), kind, 1, null);
        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        return kind switch
        {
            "intel.correlate-entry" => services.GetRequiredService<Correlation.CorrelateEntryHandler>()
                .HandleAsync(System.Text.Json.JsonSerializer.Deserialize<Correlation.CorrelateEntry>(payload, json)!, context, CancellationToken.None),
            "intel.notify-entry-changed" => services.GetRequiredService<Correlation.NotifyEntryChangedHandler>()
                .HandleAsync(System.Text.Json.JsonSerializer.Deserialize<Correlation.NotifyEntryChanged>(payload, json)!, context, CancellationToken.None),
            "intel.sync-source" => services.GetRequiredService<SyncSourceHandler>()
                .HandleAsync(System.Text.Json.JsonSerializer.Deserialize<SyncSource>(payload, json)!, context, CancellationToken.None),
            "intel.normalize-records" => services.GetRequiredService<NormalizeRecordsHandler>()
                .HandleAsync(System.Text.Json.JsonSerializer.Deserialize<NormalizeRecords>(payload, json)!, context, CancellationToken.None),
            _ => throw new InvalidOperationException($"No test runner for {kind}."),
        };
    }

    public ValueTask DisposeAsync() => Services.DisposeAsync();

    private sealed class NoBackup : IBackupWriter
    {
        public Task<string?> WriteAsync(NpgsqlConnection owner, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}

internal sealed class ScriptedProvider : ISourceProvider, IChangeLogSource
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

    public DateTimeOffset? BackfillFromSeen { get; private set; }

    public List<ChangedRecord> ChangeLog { get; } = [];

    public Dictionary<string, RawRecord> Singles { get; } = [];

    public async IAsyncEnumerable<ChangedRecord> ListChangesAsync(SyncContext context, DateTimeOffset since, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var change in ChangeLog.Where(c => c.ModifiedAt is null || c.ModifiedAt >= since))
        {
            await Task.Yield();
            yield return change;
        }
    }

    public async IAsyncEnumerable<FetchItem> FetchAsync(SyncContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CursorSeen = context.Cursor;
        ModeSeen = context.Mode;
        BackfillFromSeen = context.BackfillFrom;
        if (context.Mode == SyncMode.Single)
        {
            var single = Singles.GetValueOrDefault(context.SingleId!) ?? Items.OfType<RawRecord>().LastOrDefault(r => r.ExternalId == context.SingleId);
            if (single is null)
            {
                throw new HttpRequestException($"{context.SingleId} not found");
            }

            await Task.Yield();
            yield return single;
            yield break;
        }

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

    public NormalizationResult Normalize(RawRecord record)
    {
        using var document = System.Text.Json.JsonDocument.Parse(record.Content);
        if (document.RootElement.TryGetProperty("bad", out _))
        {
            return NormalizationResult.Quarantine("scripted bad record");
        }

        var root = document.RootElement;
        var kind = root.TryGetProperty("kind", out var k) ? k.GetString() : null;
        var aliases = root.TryGetProperty("aliases", out var list)
            ? list.EnumerateArray().Select(a => a.GetString()!.Split(':', 2)).Select(p => new Identifier(p[0], p[1])).ToList()
            : [new Identifier("TEST", record.ExternalId)];
        var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
        double? score = root.TryGetProperty("score", out var s) ? s.GetDouble() : null;
        return NormalizationResult.Success(new Observation
        {
            Kind = kind == "warning" ? ObservationKind.Warning : kind == "advisory" ? ObservationKind.Advisory : ObservationKind.Vulnerability,
            Title = title,
            Claims = kind == "warning" ? [] :
            [
                new ClaimBlock
                {
                    Aliases = aliases,
                    Title = title,
                    Metrics = score is null ? [] : [new SeverityMetric("CVSS", "3.1", "AV:N", score)],
                },
            ],
        });
    }

    public static RawRecord Json(string id, string json) => new(id, json, "application/json");
}

internal sealed class ScriptedSignalProvider : ISignalProvider
{
    public const string Key = "scripted-signals";

    public SourceDescriptor Descriptor { get; } = new()
    {
        Key = Key,
        Name = "Scripted signals",
        Kind = SourceKind.Signal,
        Operator = "Tests",
        OperatorKind = OperatorKind.Mandated,
        DefaultInterval = TimeSpan.FromHours(1),
        AllowedHosts = ["example.org"],
        Attribution = "Test data",
    };

    public List<Signal> Signals { get; } = [];

    public async IAsyncEnumerable<Signal> FetchSignalsAsync(SyncContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var signal in Signals)
        {
            await Task.Yield();
            yield return signal;
        }

        context.SaveCursor("scripted-cursor");
    }
}
