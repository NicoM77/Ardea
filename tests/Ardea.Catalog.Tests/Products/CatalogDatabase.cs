using Ardea.Catalog.Persistence;
using Ardea.Catalog.Products;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Ardea.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Ardea.Catalog.Tests.Products;

internal sealed class CatalogDatabase(PostgresServer server, string name)
{
    public static readonly IModuleDatabase Module = new CatalogModule().Database!;

    private readonly DatabaseOptions _options = new()
    {
        Host = server.Host,
        Port = server.Port,
        Database = name,
        OwnerUser = PostgresServer.OwnerUser,
        OwnerPassword = PostgresServer.OwnerPassword,
        AppPassword = "app-test-password",
    };

    public DatabaseOptions Options => _options;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<CatalogDatabase> CreateAsync(PostgresServer server) => new(server, await server.CreateDatabaseAsync("catalog"));

    public static ProductCatalog Lookup(NpgsqlConnection connection, CatalogCache cache) =>
        new(
            new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseArdeaNpgsql(connection, CatalogDbContext.Schema).Options, new TenantContext()),
            cache);

    public DatabaseMigrator Migrator(MasterDataOptions masterData) =>
        new(_options, [PlatformDatabase.Instance, Module], new NoBackup(), NullLogger<DatabaseMigrator>.Instance, masterData);

    public async Task<NpgsqlConnection> OpenOwnerAsync()
    {
        var connection = new NpgsqlConnection(_options.OwnerConnectionString("tests"));
        await connection.OpenAsync(Ct);
        return connection;
    }

    public async Task<NpgsqlConnection> OpenAppAsync()
    {
        var connection = new NpgsqlConnection(_options.AppConnectionString("tests"));
        await connection.OpenAsync(Ct);
        return connection;
    }

    private sealed class NoBackup : IBackupWriter
    {
        public Task<string?> WriteAsync(NpgsqlConnection owner, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
