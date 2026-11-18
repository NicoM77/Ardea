using Ardea.Catalog.Contracts;
using Ardea.Catalog.Persistence;
using Ardea.Catalog.Products;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Ardea.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Ardea.Catalog.Tests.Products;

/// <summary>
/// What only the real database shows: <c>migrate</c> loads the curated catalogue idempotently, never
/// touches learned entries, and the application role reads only entries in effect (master plan 4.11, 6.9).
/// </summary>
public sealed class CatalogSeedingTests(PostgresServer server)
{
    private static readonly IModuleDatabase Catalog = new CatalogModule().Database!;

    [Fact]
    public async Task Repository_catalogue_is_loaded_once_and_readable_by_the_application()
    {
        var db = await CreateAsync();
        var masterData = new MasterDataOptions(MasterDataOptions.FindDefault());
        await using var owner = await db.OpenOwnerAsync();
        await db.Migrator(masterData).MigrateAsync(owner, Ct);

        var again = await Catalog.SeedAsync(owner, masterData, Ct);

        Assert.Equal(0, again);
        await using var app = await db.OpenAppAsync();
        var catalog = Lookup(app);
        Assert.Equal(CatalogFiles.Load(CatalogFilesTests.RepositoryCatalog).Products.Count, await Scalar<long>(app, "SELECT count(*) FROM catalog.product WHERE origin = 'curated'"));
        Assert.Equal("aspnetcore", (await catalog.FindByNameAsync("Microsoft Corporation", "ASP.NET Core", Ct))?.Product.Key);
        Assert.Equal("log4j-core", (await catalog.FindByPackageAsync("maven", "org.apache.logging.log4j:log4j-core", Ct))?.Key);
        Assert.Equal(["express"], (await catalog.FindByKeyAsync("express", Ct))?.Packages.Select(p => p.Name));
        Assert.Equal("postgresql", (await catalog.ResolveKeyAsync("n/a", "PostgreSQL", null, null, Ct))?.Value);
        Assert.Equal("acme/supertool", (await catalog.ResolveKeyAsync("Acme Inc.", "SuperTool", null, null, Ct))?.Value);
    }

    [Fact]
    public async Task Seeding_follows_the_files_for_curated_rows_and_leaves_learned_rows_alone()
    {
        var db = await CreateAsync();
        await using var owner = await db.OpenOwnerAsync();
        await db.Migrator(Files(Product("alpha", "Alpha"), Product("beta", "Beta"))).MigrateAsync(owner, Ct);
        await Execute(owner, """
            INSERT INTO catalog.product (key, name, kind, cpe_pairs, purls, aliases, version_scheme, related_keys, image_names, origin, state, confidence, learned_by)
            VALUES ('learned-tool', 'Learned Tool', 'library', '{acme:learned_tool}', '{}', '[]', 'numeric', '{}', '{}', 'learned', 'active', 'high', 'model'),
                   ('gamma', 'Gamma (learned)', 'library', '{}', '{}', '[]', 'numeric', '{}', '{}', 'learned', 'pending', 'medium', 'model')
            """);

        var changed = await Catalog.SeedAsync(owner, Files(Product("alpha", "Alpha Renamed"), Product("gamma", "Gamma")), Ct);

        Assert.Equal(3, changed);
        Assert.Equal(
            ["alpha:Alpha Renamed:curated", "gamma:Gamma:curated", "learned-tool:Learned Tool:learned"],
            await Rows(owner, "SELECT key || ':' || name || ':' || origin FROM catalog.product ORDER BY key"));
        Assert.Equal(0, await Catalog.SeedAsync(owner, Files(Product("alpha", "Alpha Renamed"), Product("gamma", "Gamma")), Ct));
    }

    [Fact]
    public async Task Empty_or_broken_catalogue_stops_migrate_and_keeps_every_row()
    {
        var db = await CreateAsync();
        await using var owner = await db.OpenOwnerAsync();
        await db.Migrator(Files(Product("alpha", "Alpha"))).MigrateAsync(owner, Ct);

        var empty = new MasterDataOptions(TempCatalog.Write([]));
        Directory.CreateDirectory(Path.Combine(empty.Directory, CatalogFiles.Folder));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Catalog.SeedAsync(owner, empty, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Catalog.SeedAsync(owner, Files(Product("alpha", "Alpha", scheme: "debian")), Ct));

        Assert.Equal(["alpha"], await Rows(owner, "SELECT key FROM catalog.product"));
    }

    [Fact]
    public async Task Lookups_see_learned_entries_only_while_active_and_learned_aliases()
    {
        var db = await CreateAsync();
        await using var owner = await db.OpenOwnerAsync();
        await db.Migrator(Files(Product("alpha", "Alpha"))).MigrateAsync(owner, Ct);
        await Execute(owner, """
            INSERT INTO catalog.product (key, name, vendor, kind, cpe_pairs, purls, aliases, version_scheme, related_keys, image_names, origin, state)
            VALUES ('active-tool', 'Active Tool', 'Acme', 'library', '{acme:active_tool}', '{}', '[]', 'numeric', '{}', '{}', 'learned', 'active'),
                   ('pending-tool', 'Pending Tool', 'Acme', 'library', '{acme:pending_tool}', '{}', '[]', 'numeric', '{}', '{}', 'learned', 'pending'),
                   ('withdrawn-tool', 'Withdrawn Tool', 'Acme', 'library', '{}', '{}', '[]', 'numeric', '{}', '{}', 'learned', 'withdrawn');
            INSERT INTO catalog.unknown_product (raw_key, vendor, product, cpe_pairs, record_count, in_kev, outcome, product_key)
            VALUES ('acme/alphaserver', 'Acme', 'Alpha Server', '{}', 3, false, 'alias', 'alpha'),
                   ('acme/betaserver', 'Acme', 'Beta Server', '{}', 2, false, 'open', null);
            """);

        await using var app = await db.OpenAppAsync();
        var catalog = Lookup(app);

        Assert.Equal(ProductOrigin.Learned, (await catalog.FindByCpeAsync(new("acme", "active_tool"), Ct))?.Origin);
        Assert.Null(await catalog.FindByCpeAsync(new("acme", "pending_tool"), Ct));
        Assert.Null(await catalog.FindByKeyAsync("withdrawn-tool", Ct));
        Assert.Equal("alpha", (await catalog.FindByNameAsync("Acme Inc.", "Alpha Server", Ct))?.Product.Key);
        Assert.Equal("acme/betaserver", (await catalog.ResolveKeyAsync("Acme", "Beta Server", null, null, Ct))?.Value);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Product(string key, string name, string scheme = "semver") =>
        $$"""{ "key": "{{key}}", "name": "{{name}}", "kind": "library", "versionScheme": "{{scheme}}", "cpe": ["acme:{{key}}"] }""";

    private static MasterDataOptions Files(params string[] products)
    {
        var root = TempCatalog.Write([]);
        var folder = Path.Combine(root, CatalogFiles.Folder);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "acme.json"), $$"""{ "vendor": "Acme", "products": [{{string.Join(", ", products)}}] }""");
        return new MasterDataOptions(root);
    }

    private static ProductCatalog Lookup(NpgsqlConnection connection) =>
        new(
            new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseArdeaNpgsql(connection, CatalogDbContext.Schema).Options, new TenantContext()),
            new CatalogCache(TimeProvider.System));

    private async Task<Database> CreateAsync() => new(server, await server.CreateDatabaseAsync("catalog"));

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static async Task<List<string>> Rows(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private sealed class Database(PostgresServer server, string name)
    {
        private readonly DatabaseOptions _options = new()
        {
            Host = server.Host,
            Port = server.Port,
            Database = name,
            OwnerUser = PostgresServer.OwnerUser,
            OwnerPassword = PostgresServer.OwnerPassword,
            AppPassword = "app-test-password",
        };

        public DatabaseMigrator Migrator(MasterDataOptions masterData) =>
            new(_options, [PlatformDatabase.Instance, Catalog], new NoBackup(), NullLogger<DatabaseMigrator>.Instance, masterData);

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
    }

    private sealed class NoBackup : IBackupWriter
    {
        public Task<string?> WriteAsync(NpgsqlConnection owner, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
