using System.Data.Common;
using Ardea.Platform.Persistence;
using Ardea.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Ardea.Platform.Tests.Persistence;

/// <summary>What only the real database can show: roles, rights and the migration path (master plan 9.2, 12.2).</summary>
public sealed class DatabaseMigratorTests(PostgresServer server)
{
    [Fact]
    public async Task Fresh_database_is_migrated_without_a_dump()
    {
        var db = await MigrationDatabase.CreateAsync(server);
        await using var owner = await db.OpenOwnerAsync();

        var outcome = await db.Migrator().MigrateAsync(owner, TestContext.Current.CancellationToken);

        Assert.True(outcome.Applied > 0);
        Assert.Null(outcome.DumpPath);
    }

    [Fact]
    public async Task Second_run_applies_nothing()
    {
        var db = await MigrationDatabase.CreateAsync(server);
        await using var owner = await db.OpenOwnerAsync();
        await db.Migrator().MigrateAsync(owner, TestContext.Current.CancellationToken);

        var second = await db.Migrator().MigrateAsync(owner, TestContext.Current.CancellationToken);

        Assert.Equal(0, second.Applied);
        Assert.Null(second.DumpPath);
    }

    [Fact]
    public async Task Application_role_works_on_data_but_owns_nothing()
    {
        var db = await MigrationDatabase.CreateAsync(server);
        await using (var owner = await db.OpenOwnerAsync())
        {
            await db.Migrator().MigrateAsync(owner, TestContext.Current.CancellationToken);
        }

        await using var app = await db.OpenAppAsync();
        await Execute(app, "INSERT INTO platform.job (kind, payload, run_at, priority, state, attempts, max_attempts, timeout_seconds) VALUES ('test', '{}', now(), 1, 'pending', 0, 5, 60)");
        await Execute(app, "UPDATE platform.job SET attempts = 1");

        var role = await Scalar<bool>(app, "SELECT rolsuper OR rolbypassrls OR rolcreaterole OR rolcreatedb FROM pg_roles WHERE rolname = current_user");
        Assert.False(role);
        await AssertDenied(app, "CREATE TABLE platform.intruder (id int)");
        await AssertDenied(app, "DROP TABLE platform.job");
    }

    [Fact]
    public async Task Audit_protocol_is_append_only_for_the_application()
    {
        var db = await MigrationDatabase.CreateAsync(server);
        await using (var owner = await db.OpenOwnerAsync())
        {
            await db.Migrator().MigrateAsync(owner, TestContext.Current.CancellationToken);
        }

        await using var app = await db.OpenAppAsync();
        await Execute(app, "INSERT INTO platform.audit_event (action, origin) VALUES ('test.done', 'tests')");

        await AssertDenied(app, "UPDATE platform.audit_event SET action = 'rewritten'");
        await AssertDenied(app, "DELETE FROM platform.audit_event");
        await AssertDenied(app, "DELETE FROM platform.__ef_migrations_history");
    }

    [Fact]
    public async Task Schema_check_reports_a_database_that_is_behind()
    {
        var db = await MigrationDatabase.CreateAsync(server);
        await using var owner = await db.OpenOwnerAsync();
        await db.Migrator().EnsureAppRoleAsync(owner, TestContext.Current.CancellationToken);

        await using (var app = await db.OpenAppAsync())
        {
            Assert.Equal(["platform"], await db.Migrator().FindSchemasBehindAsync(app, TestContext.Current.CancellationToken));
        }

        await db.Migrator().MigrateAsync(owner, TestContext.Current.CancellationToken);
        await using var current = await db.OpenAppAsync();
        Assert.Empty(await db.Migrator().FindSchemasBehindAsync(current, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Master_data_is_loaded_after_the_migrations_on_every_run()
    {
        var db = await MigrationDatabase.CreateAsync(server);
        await using var owner = await db.OpenOwnerAsync();
        var seeding = new SeedingDatabase();
        var masterData = new MasterDataOptions("/master-data");
        var migrator = new DatabaseMigrator(db.Options, [seeding], db.Backup(), NullLogger<DatabaseMigrator>.Instance, masterData);

        await migrator.MigrateAsync(owner, TestContext.Current.CancellationToken);
        var second = await migrator.MigrateAsync(owner, TestContext.Current.CancellationToken);

        Assert.Equal(0, second.Applied);
        Assert.Equal([true, true], seeding.SawMigratedSchema);
        Assert.All(seeding.Directories, d => Assert.Equal("/master-data", d));
    }

    /// <summary>The platform schema with a seed that records what it saw.</summary>
    private sealed class SeedingDatabase : IModuleDatabase
    {
        public List<bool> SawMigratedSchema { get; } = [];

        public List<string> Directories { get; } = [];

        public string Schema => PlatformDatabase.Instance.Schema;

        public DbContext CreateContext(DbConnection connection) =>
            PlatformDatabase.Instance.CreateContext(connection);

        public async Task<int> SeedAsync(DbConnection connection, MasterDataOptions masterData, CancellationToken cancellationToken)
        {
            SawMigratedSchema.Add(await Scalar<bool>((NpgsqlConnection)connection, "SELECT to_regclass('platform.job') IS NOT NULL"));
            Directories.Add(masterData.Directory);
            return 1;
        }
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task AssertDenied(NpgsqlConnection connection, string sql)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => Execute(connection, sql));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }
}
