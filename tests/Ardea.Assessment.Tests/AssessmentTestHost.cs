using System.Text.Json;
using Ardea.Assessment.Assessing;
using Ardea.Assets.Features;
using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Persistence;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Ardea.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Ardea.Assessment.Tests;

/// <summary>
/// Catalogue, Assets, Intel and Assessment on a freshly migrated database, the way the worker composes
/// them. Profiles are written through Assets' own key computation, entries through Intel's tables, and
/// jobs run like the worker runs them: one scope, the job's tenant, one committed unit of work each.
/// </summary>
internal sealed class AssessmentTestHost : IAsyncDisposable
{
    private AssessmentTestHost(ServiceProvider services, DatabaseOptions database, FakeTimeProvider clock, Guid sourceId)
    {
        Services = services;
        Database = database;
        Clock = clock;
        SourceId = sourceId;
    }

    public ServiceProvider Services { get; }

    public DatabaseOptions Database { get; }

    public FakeTimeProvider Clock { get; }

    /// <summary>The source every test entry comes from.</summary>
    public Guid SourceId { get; }

    /// <summary>The organisations' language and AI data policy; <c>local_only</c> unless a test sets another.</summary>
    public Estimating.TestOrganizations Organizations => Services.GetRequiredService<Estimating.TestOrganizations>();

    /// <param name="server">The database server.</param>
    /// <param name="configure">Replaces services after the modules registered theirs, e.g. the AI providers.</param>
    /// <param name="settings">Configuration values, e.g. the AI limits.</param>
    public static async Task<AssessmentTestHost> StartAsync(
        PostgresServer server, Action<IServiceCollection>? configure = null, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var name = await server.CreateDatabaseAsync("assess");
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

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(database);
        services.AddSingleton(_ => NpgsqlDataSource.Create(database.AppConnectionString("assess-tests")));
        services.AddUnitOfWork();
        services.AddJobQueue();
        new Catalog.CatalogModule().AddServices(services, configuration);
        new Assets.AssetsModule().AddServices(services, configuration);
        new Intel.IntelModule().AddServices(services, configuration);
        new AssessmentModule().AddServices(services, configuration);
        services.AddSingleton<Estimating.TestOrganizations>();
        services.AddSingleton<Iam.Contracts.IOrganizationProfiles>(sp => sp.GetRequiredService<Estimating.TestOrganizations>());
        configure?.Invoke(services);
        var built = services.BuildServiceProvider(validateScopes: true);

        var sourceId = Guid.CreateVersion7();
        await using (var scope = built.CreateAsyncScope())
        {
            var intel = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
            intel.Sources.Add(new SourceRow
            {
                Id = sourceId,
                Key = "test-source",
                Name = "Test source",
                Kind = "record",
                Operator = "Tests",
                OperatorKind = "community",
                Adapter = "test",
                Attribution = "Test data",
                TermsMode = "full",
                Health = SourceHealth.Healthy,
                NextSyncAt = clock.GetUtcNow(),
            });
            await intel.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
        }

        return new AssessmentTestHost(built, database, clock, sourceId);
    }

    /// <summary>An organisation id; organisations live in Iam, which this host leaves out.</summary>
    public static Guid NewOrganization() => Guid.CreateVersion7();

    public async Task<Guid> AddProjectAsync(Guid organization, string name = "Portal", string criticality = "normal", string exposure = "internal")
    {
        var id = Guid.CreateVersion7();
        await InOrganizationAsync(organization, async services =>
        {
            var db = services.GetRequiredService<AssetsDbContext>();
            db.Projects.Add(new Project { Id = id, Name = name, Criticality = criticality, Exposure = exposure, CreatedAt = Clock.GetUtcNow(), ChangedAt = Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        });
        return id;
    }

    /// <summary>The organisation's stack (master plan 17.2), the system project Assets creates with the organisation.</summary>
    public async Task<Guid> AddStackAsync(Guid organization)
    {
        var id = Guid.CreateVersion7();
        await InOrganizationAsync(organization, async services =>
        {
            var db = services.GetRequiredService<AssetsDbContext>();
            db.Projects.Add(new Project { Id = id, Kind = Project.StackKind, Name = Project.StackName, CreatedAt = Clock.GetUtcNow(), ChangedAt = Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        });
        return id;
    }

    /// <summary>Adds a component and its search keys as the profile endpoints do.</summary>
    public async Task<Guid> AddComponentAsync(Guid organization, Guid project, Action<Component> configure)
    {
        var id = Guid.CreateVersion7();
        await InOrganizationAsync(organization, async services =>
        {
            var db = services.GetRequiredService<AssetsDbContext>();
            var component = new Component { Id = id, ProjectId = project, Kind = "other", CreatedAt = Clock.GetUtcNow(), ChangedAt = Clock.GetUtcNow() };
            configure(component);
            WithPrecision(component);
            db.Components.Add(component);
            await db.SaveChangesAsync();
            await ComponentKeys.RefreshAsync(db, services.GetRequiredService<IProductCatalog>(), [component], CancellationToken.None);
        });
        return id;
    }

    /// <summary>Changes a component and its keys as the profile endpoints do.</summary>
    public Task ChangeComponentAsync(Guid organization, Guid component, Action<Component> change) =>
        InOrganizationAsync(organization, async services =>
        {
            var db = services.GetRequiredService<AssetsDbContext>();
            var row = db.Components.Single(c => c.Id == component);
            change(row);
            WithPrecision(row);
            await db.SaveChangesAsync();
            await ComponentKeys.RefreshAsync(db, services.GetRequiredService<IProductCatalog>(), [row], CancellationToken.None);
        });

    /// <summary>The precision that follows from the version, as the profile endpoints set it, unless the test set one that fits (ADR 046).</summary>
    internal static void WithPrecision(Component component)
    {
        if (!VersionPrecisions.Fits(component.VersionPrecision, component.Version))
        {
            component.VersionPrecision = VersionPrecisions.Of(component.Version, component.Purl, component.Origin);
        }
    }

    /// <summary>Writes a consolidated entry with its affected products, as the correlation would.</summary>
    public async Task<Guid> AddEntryAsync(TestEntry entry)
    {
        var id = Guid.CreateVersion7();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        db.Entries.Add(new EntryRow
        {
            Id = id,
            Kind = "vulnerability",
            PrimaryKey = entry.Key,
            Title = entry.Key,
            State = entry.State,
            PublishedAt = Clock.GetUtcNow().AddDays(-1),
            ChangedAt = entry.ChangedAt ?? Clock.GetUtcNow(),
            Revision = 1,
            MaterialFingerprint = "m",
            RemediationFingerprint = "r",
            CvssScore = entry.Score,
            Exploitation = entry.Exploitation,
            EpssClass = entry.Epss,
            AuthorityWarning = entry.Authority,
            FixAvailable = entry.Affected.Any(a => a.Fixed is not null),
            Description = entry.Description,
            Details = JsonSerializer.SerializeToDocument(new { solutions = entry.Solutions, workarounds = Array.Empty<string>() }),
            Provenance = JsonDocument.Parse(entry.Description is null ? "{}" : """{"description": "test-source"}"""),
        });
        foreach (var a in entry.Affected)
        {
            db.EntryAffected.Add(new EntryAffectedRow
            {
                Id = Guid.CreateVersion7(),
                EntryId = id,
                SourceId = a.SourceId ?? SourceId,
                Vendor = a.Vendor,
                Product = a.Product,
                ProductKey = a.ProductKey,
                CpeVendor = a.CpeVendor,
                CpeProduct = a.CpeProduct,
                PurlType = a.PurlType,
                PurlName = a.PurlName,
                VersionScheme = a.Scheme,
                Introduced = a.Introduced,
                Fixed = a.Fixed,
                LastAffected = a.LastAffected,
                Exact = a.Exact,
                Status = a.Status,
                DefaultStatus = a.DefaultStatus,
            });
        }

        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
        return id;
    }

    /// <summary>Intel's material change, as <c>NotifyEntryChanged</c> tells the subscribers.</summary>
    public Task EntryChangedAsync(Guid entryId) =>
        InSystemAsync(services => services.GetRequiredService<Intel.Correlation.NotifyEntryChangedHandler>()
            .HandleAsync(new Intel.Correlation.NotifyEntryChanged(entryId), Context(null, "intel.notify-entry-changed"), CancellationToken.None));

    /// <summary>Assets' profile change, as <c>ProjectProfileChanged</c> tells the subscribers.</summary>
    public Task ProfileChangedAsync(Guid organization, Guid project) =>
        InOrganizationAsync(organization, services => services.GetRequiredService<ProjectProfileChangedHandler>()
            .HandleAsync(new ProjectProfileChanged(project), Context(organization, ProjectProfileChanged.Kind), CancellationToken.None));

    /// <summary>Runs every waiting job of the assessment, the way the worker would, until none is left.</summary>
    public async Task RunJobsAsync()
    {
        for (var round = 0; round < 10; round++)
        {
            await using var connection = new NpgsqlConnection(Database.OwnerConnectionString("tests"));
            await connection.OpenAsync();
            await using var select = new NpgsqlCommand("SELECT id, kind, payload::text, org_id FROM platform.job WHERE state = 'pending' AND kind LIKE 'assess.%' ORDER BY id", connection);
            var due = new List<(Guid Id, string Kind, string Payload, Guid? Organization)>();
            await using (var reader = await select.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    due.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetGuid(3)));
                }
            }

            if (due.Count == 0)
            {
                return;
            }

            foreach (var (id, kind, payload, organization) in due)
            {
                await using (var done = new NpgsqlCommand("UPDATE platform.job SET state = 'succeeded', finished_at = now() WHERE id = $1", connection))
                {
                    done.Parameters.Add(new NpgsqlParameter { Value = id });
                    await done.ExecuteNonQueryAsync();
                }

                await RunAsync(kind, payload, organization);
            }
        }

        throw new InvalidOperationException("Jobs keep coming.");
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.OwnerConnectionString("tests"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Rows of a query as tab-separated text, for compact assertions.</summary>
    public async Task<IReadOnlyList<string>> RowsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.OwnerConnectionString("tests"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(string.Join('\t', Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "-" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    public async Task InOrganizationAsync(Guid organization, Func<IServiceProvider, Task> work)
    {
        // Every unit of work happens a moment after the previous one, so the history has an order.
        Clock.Advance(TimeSpan.FromSeconds(1));
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetOrganization(organization);
        await work(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
    }

    public async Task InSystemAsync(Func<IServiceProvider, Task> work)
    {
        Clock.Advance(TimeSpan.FromSeconds(1));
        await using var scope = Services.CreateAsyncScope();
        await work(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync() => Services.DisposeAsync();

    private Task RunAsync(string kind, string payload, Guid? organization)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Task Run<TJob, THandler>(IServiceProvider services)
            where TJob : IJob
            where THandler : IJobHandler<TJob> =>
            services.GetRequiredService<THandler>().HandleAsync(JsonSerializer.Deserialize<TJob>(payload, json)!, Context(organization, kind), CancellationToken.None);

        Func<IServiceProvider, Task> work = kind switch
        {
            "assess.match-entry" => Run<MatchEntry, MatchEntryHandler>,
            "assess.assess-alert" => Run<AssessAlert, AssessAlertHandler>,
            "assess.match-project" => Run<MatchProject, MatchProjectHandler>,
            "assess.estimate-alert" => Run<Ardea.Assessment.Estimating.EstimateAlert, Ardea.Assessment.Estimating.EstimateAlertHandler>,
            _ => throw new InvalidOperationException($"No test runner for {kind}."),
        };
        return organization is { } org ? InOrganizationAsync(org, work) : InSystemAsync(work);
    }

    private static JobContext Context(Guid? organization, string kind) => new(Guid.CreateVersion7(), kind, 1, organization);

    private sealed class NoBackup : IBackupWriter
    {
        public Task<string?> WriteAsync(NpgsqlConnection owner, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}

/// <summary>An entry for a test: severity, exploitation and the affected products.</summary>
internal sealed record TestEntry(string Key, params TestAffected[] Affected)
{
    public string State { get; init; } = "published";

    public double? Score { get; init; } = 7.5;

    public string Exploitation { get; init; } = "none";

    public string? Epss { get; init; }

    public bool Authority { get; init; }

    public DateTimeOffset? ChangedAt { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> Solutions { get; init; } = [];
}

/// <summary>One version statement about an affected product.</summary>
internal sealed record TestAffected
{
    public Guid? SourceId { get; init; }

    public string? Vendor { get; init; }

    public string? Product { get; init; }

    public string? ProductKey { get; init; }

    public string? CpeVendor { get; init; }

    public string? CpeProduct { get; init; }

    public string? PurlType { get; init; }

    public string? PurlName { get; init; }

    public string? Scheme { get; init; }

    public string? Introduced { get; init; }

    public string? Fixed { get; init; }

    public string? LastAffected { get; init; }

    public string? Exact { get; init; }

    public string Status { get; init; } = "affected";

    public string DefaultStatus { get; init; } = "unknown";
}
