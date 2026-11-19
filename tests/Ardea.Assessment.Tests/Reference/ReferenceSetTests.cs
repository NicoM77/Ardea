using System.Diagnostics;
using System.Text.Json;
using Ardea.Assets.Features;
using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Persistence;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sources.CveProgram;
using Ardea.Intel.Sync;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Ardea.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Ardea.Assessment.Tests.Reference;

/// <summary>
/// The evaluation run of master plan 7.9 (task 3.7): every pair of the reference set through the real
/// pipeline, rules only. The records are stored as the CVE Program source would store them, normalised by
/// its parser, correlated into entries and matched against one organisation per pair; then the findings'
/// levels are compared with the labels. The build fails when the thresholds are not met.
/// </summary>
public sealed class ReferenceSetTests(PostgresServer server)
{
    [Fact(Timeout = 300_000)]
    public async Task The_rules_meet_the_thresholds_of_the_reference_set()
    {
        var set = ReferenceSet.Load(ReferenceSet.Folder);
        var watch = Stopwatch.StartNew();
        var outcomes = await EvaluateAsync(server, set);
        var evaluation = new ReferenceEvaluation(outcomes);
        var report = evaluation.Report(watch.Elapsed);

        // The report is the calibration's working paper; CI keeps it with the test results.
        var output = Path.Combine(AppContext.BaseDirectory, "reference-report.md");
        await File.WriteAllTextAsync(output, report, TestContext.Current.CancellationToken);
        TestContext.Current.SendDiagnosticMessage($"Reference set report: {output}");

        var failures = evaluation.Failures();
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Prepend("The reference set's thresholds are not met (master plan 7.9):")) + Environment.NewLine + report);
    }

    /// <summary>
    /// The spike week (master plan 14, phase 3 acceptance): all 6 452 records of 11 to 17 November 2026 against
    /// the three example projects of the requirements, each with the versions installed before that week's
    /// security updates. One organisation with all three gives at most 8 open actions.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task The_spike_week_gives_at_most_eight_open_actions_for_the_three_example_projects()
    {
        var watch = Stopwatch.StartNew();
        await using var host = await ReferenceHost.StartAsync(server);
        var organization = Guid.CreateVersion7();
        foreach (var (name, attributes, components) in SpikeWeek.Projects)
        {
            var project = await host.AddProjectAsync(organization, name, attributes);
            foreach (var component in components)
            {
                await host.AddComponentAsync(organization, project, component);
            }
        }

        await host.RunJobsAsync();
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        await host.StoreRecordsAsync(SpikeWeek.Records());
        await host.RunJobsAsync();

        var actions = await host.RowsAsync("""
            SELECT p.name, coalesce(c.product_key, c.custom_name), c.version, r.level, r.open_findings
            FROM assess.remediation r JOIN assets.component c ON c.id = r.component_id JOIN assets.project p ON p.id = r.project_id
            WHERE r.status <> 'closed' ORDER BY r.level DESC, p.name, 2
            """);
        var summary = $"Spike week: {actions.Count} open actions in {watch.Elapsed.TotalSeconds:F0} s{Environment.NewLine}{string.Join(Environment.NewLine, actions)}";
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "spike-week-report.txt"), summary, TestContext.Current.CancellationToken);
        TestContext.Current.SendDiagnosticMessage(summary);
        Assert.True(actions.Count <= 8, $"{actions.Count} open actions, at most 8:{Environment.NewLine}{string.Join(Environment.NewLine, actions)}");
        Assert.NotEmpty(actions);
    }

    [Fact]
    public void Every_pair_names_a_record_of_the_set_and_a_valid_label()
    {
        var set = ReferenceSet.Load(ReferenceSet.Folder);
        Assert.Empty(set.Pairs.Where(p => !set.Records.ContainsKey(p.Record)).Select(p => p.Id));
        Assert.Equal(set.Pairs.Count, set.Pairs.Select(p => p.Id).Distinct().Count());
        Assert.Empty(set.Pairs.Where(p => p.Label is { } l
            && (l.Verdict is not (ReferenceLabel.Affected or ReferenceLabel.NotAffected or ReferenceLabel.Undecidable)
                || l.Level is < 1 or > 5
                || (l.Verdict == ReferenceLabel.NotAffected) != (l.Level == 1)
                || string.IsNullOrWhiteSpace(l.Reason)))
            .Select(p => p.Id));
        Assert.Empty(set.Pairs.Where(p => (p.Component.ProductKey is null) == (p.Component.Purl is null)).Select(p => p.Id));
    }

    private static async Task<IReadOnlyList<ReferenceOutcome>> EvaluateAsync(PostgresServer server, ReferenceSet set)
    {
        await using var host = await ReferenceHost.StartAsync(server);
        var projects = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var pair in set.Pairs)
        {
            projects[pair.Id] = await host.AddPairAsync(pair);
        }

        // Profiles first, then the records: the forward search, as when a new record arrives.
        await host.RunJobsAsync();
        await host.StoreRecordsAsync(set.Records.Select(r => new RawRecord(r.Key, r.Value, "application/json")).ToList());
        await host.RunJobsAsync();

        var findings = await host.FindingsAsync();
        return [.. set.Pairs.Select(pair =>
        {
            var found = findings.Where(f => f.ProjectId == projects[pair.Id] && f.Entry == pair.Record).OrderByDescending(f => f.Level).FirstOrDefault();
            return found is null
                ? new ReferenceOutcome(pair, 1, null, null, null)
                : new ReferenceOutcome(pair, found.Level, found.MatchType, found.VersionVerdict, found.VersionReason);
        })];
    }
}

/// <summary>Catalogue, Assets, Intel with the CVE Program's parser, and Assessment on a fresh database.</summary>
internal sealed class ReferenceHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly DatabaseOptions _database;
    private readonly TimeProvider _clock;

    private ReferenceHost(ServiceProvider services, DatabaseOptions database, TimeProvider clock)
    {
        _services = services;
        _database = database;
        _clock = clock;
    }

    public static async Task<ReferenceHost> StartAsync(PostgresServer server)
    {
        var name = await server.CreateDatabaseAsync("reference");
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
            await new DatabaseMigrator(
                    database,
                    [PlatformDatabase.Instance, new Catalog.CatalogModule().Database!, new Assets.AssetsModule().Database!, new Intel.IntelModule().Database!, new AssessmentModule().Database!],
                    new NoBackup(),
                    NullLogger<DatabaseMigrator>.Instance)
                .MigrateAsync(owner, CancellationToken.None);
        }

        // The day after the spike week: every record is recent, none is in the future.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SCRATCH_DIR"] = Path.Combine(Path.GetTempPath(), "ardea-reference-tests"),
        }).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(database);
        services.AddSingleton(_ => NpgsqlDataSource.Create(database.AppConnectionString("reference-tests")));
        services.AddUnitOfWork();
        services.AddJobQueue();
        services.AddSingleton<ISourceProvider, CveProgramProvider>();
        new Catalog.CatalogModule().AddServices(services, configuration);
        new Assets.AssetsModule().AddServices(services, configuration);
        new Intel.IntelModule().AddServices(services, configuration);
        new Intel.IntelModule().AddWorkerServices(services, configuration);
        new AssessmentModule().AddServices(services, configuration);
        var built = services.BuildServiceProvider(validateScopes: true);

        // Registers the CVE Program as a source, as the worker does at start. Nothing syncs: no scheduler runs.
        await built.GetRequiredService<IEnumerable<IHostedService>>().OfType<SourceRegistrar>().Single().StartAsync(CancellationToken.None);
        return new ReferenceHost(built, database, clock);
    }

    /// <summary>One organisation with one project and the pair's component, written as the profile endpoints do.</summary>
    public async Task<Guid> AddPairAsync(ReferencePair pair)
    {
        var organization = Guid.CreateVersion7();
        var project = await AddProjectAsync(organization, pair.Id, pair.Project);
        await AddComponentAsync(organization, project, pair.Component);
        return project;
    }

    public async Task<Guid> AddProjectAsync(Guid organization, string name, ReferenceProject attributes)
    {
        var project = Guid.CreateVersion7();
        await InOrganizationAsync(organization, services =>
        {
            var now = _clock.GetUtcNow();
            services.GetRequiredService<AssetsDbContext>().Projects.Add(new Project
            {
                Id = project,
                Name = name,
                Criticality = attributes.Criticality,
                Exposure = attributes.Exposure,
                CreatedAt = now,
                ChangedAt = now,
            });
            return services.GetRequiredService<AssetsDbContext>().SaveChangesAsync();
        });
        return project;
    }

    public Task AddComponentAsync(Guid organization, Guid project, ReferenceComponent c) =>
        InOrganizationAsync(organization, async services =>
        {
            var db = services.GetRequiredService<AssetsDbContext>();
            var now = _clock.GetUtcNow();
            var component = new Component
            {
                Id = Guid.CreateVersion7(),
                ProjectId = project,
                ProductKey = c.ProductKey,
                CustomName = c.ProductKey is null ? c.Name : null,
                Kind = c.Kind,
                Version = c.Version,
                Packaging = c.Packaging,
                Purl = c.Purl,
                DependencyScope = c.DependencyScope,
                CreatedAt = now,
                ChangedAt = now,
            };
            db.Components.Add(component);
            await db.SaveChangesAsync();
            await ComponentKeys.RefreshAsync(db, services.GetRequiredService<IProductCatalog>(), [component], CancellationToken.None);
        });

    private async Task InOrganizationAsync(Guid organization, Func<IServiceProvider, Task> work)
    {
        await using var scope = _services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetOrganization(organization);
        await work(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
    }

    /// <summary>Runs a query as the owner and returns its rows as tab-separated text.</summary>
    public async Task<IReadOnlyList<string>> RowsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.OwnerConnectionString("tests"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(string.Join('\t', Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? string.Empty : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    /// <summary>Stores the records as the CVE Program source's sync would, and queues their normalisation.</summary>
    public async Task StoreRecordsAsync(IReadOnlyList<RawRecord> records)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var source = await db.Sources.SingleAsync(s => s.Key == CveProgramProvider.Key);
        await RawRecordStore.StoreAsync(db, source.Id, records, _clock.GetUtcNow(), CancellationToken.None);
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IJobQueue>().EnqueueAsync(
            new NormalizeRecords(CveProgramProvider.Key), NormalizeRecords.Options(CveProgramProvider.Key), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
    }

    /// <summary>
    /// Runs every waiting job, whatever its kind, the way the worker runs it: its own scope, its tenant,
    /// one committed unit of work; until none is left.
    /// </summary>
    public async Task RunJobsAsync()
    {
        var registry = _services.GetRequiredService<JobRegistry>();
        for (var round = 0; round < 50; round++)
        {
            var due = new List<(Guid Id, string Kind, string Payload, Guid? Organization)>();
            await using (var connection = new NpgsqlConnection(_database.OwnerConnectionString("tests")))
            {
                await connection.OpenAsync();
                await using var claim = new NpgsqlCommand(
                    "UPDATE platform.job SET state = 'succeeded', finished_at = now() WHERE state = 'pending' RETURNING id, kind, payload::text, org_id",
                    connection);
                await using var reader = await claim.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    due.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetGuid(3)));
                }
            }

            if (due.Count == 0)
            {
                return;
            }

            foreach (var (id, kind, payload, organization) in due.OrderBy(d => d.Id))
            {
                if (!registry.TryGet(kind, out var registration))
                {
                    throw new InvalidOperationException($"No handler for job kind {kind}.");
                }

                await using var scope = _services.CreateAsyncScope();
                if (organization is { } org)
                {
                    scope.ServiceProvider.GetRequiredService<TenantContext>().SetOrganization(org);
                }

                using var json = JsonDocument.Parse(payload);
                await registration.Invoke(scope.ServiceProvider, json, new JobContext(id, kind, 1, organization), CancellationToken.None);
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
            }
        }

        throw new InvalidOperationException("Jobs keep coming.");
    }

    /// <summary>Every finding with its entry's CVE id.</summary>
    public async Task<IReadOnlyList<FoundFinding>> FindingsAsync()
    {
        await using var connection = new NpgsqlConnection(_database.OwnerConnectionString("tests"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT f.project_id, e.primary_key, f.level, f.match_type, f.version_verdict, f.version_reason FROM assess.finding f JOIN intel.security_entry e ON e.id = f.entry_id",
            connection);
        var findings = new List<FoundFinding>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            findings.Add(new FoundFinding(
                reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return findings;
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    internal sealed record FoundFinding(Guid ProjectId, string Entry, int Level, string MatchType, string? VersionVerdict, string? VersionReason);

    private sealed class NoBackup : IBackupWriter
    {
        public Task<string?> WriteAsync(NpgsqlConnection owner, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
