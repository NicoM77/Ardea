using Ardea.Platform.Persistence;
using Ardea.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Ardea.Host.Tests;

public sealed class PreReleaseRestoreTests(PostgresServer server)
{
    private const string Cutoff = "20261119";

    [Fact]
    public async Task Pre_release_dump_migrates_to_this_build_and_restores_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.Zero));

        var oldName = await server.CreateDatabaseAsync("prerelease");
        var oldOptions = Options(oldName);
        await using (var owner = await OpenOwnerAsync(oldOptions))
        {
            await new DatabaseMigrator(oldOptions, Modules.Databases, new NoDump(), NullLogger<DatabaseMigrator>.Instance).EnsureAppRoleAsync(owner, ct);
            await MigrateBeforeCutoffAsync(owner, ct);
            await InsertSampleRowsAsync(owner, ct);
        }

        time.Advance(TimeSpan.FromMinutes(1));
        string preReleaseDump;
        await using (var owner = await OpenOwnerAsync(oldOptions))
        {
            preReleaseDump = (await new ServerSideBackupWriter(oldOptions, time, NullLogger<ServerSideBackupWriter>.Instance).WriteAsync(owner, ct))!;
        }

        var name = await server.CreateDatabaseAsync("restored");
        var options = Options(name);
        await RestoreAsync(name, preReleaseDump, ct);
        await using (var owner = await OpenOwnerAsync(options))
        {
            Assert.Equal(1, await CountAsync(owner, "platform.setting", ct));
            Assert.False(await SchemaExistsAsync(owner, "assess", ct));
            Assert.NotEmpty(await Migrator(options, time).FindSchemasBehindAsync(owner, ct));
        }

        time.Advance(TimeSpan.FromMinutes(1));
        MigrationOutcome outcome;
        await using (var owner = await OpenOwnerAsync(options))
        {
            outcome = await Migrator(options, time).MigrateAsync(owner, ct);
            Assert.True(outcome.Applied > 0);
            Assert.NotNull(outcome.DumpPath);
            Assert.Empty(await Migrator(options, time).FindSchemasBehindAsync(owner, ct));
            await AssertSampleRowsAsync(owner, ct);
            Assert.True(await SchemaExistsAsync(owner, "assess", ct));
        }

        await using (var app = new NpgsqlConnection(options.AppConnectionString("tests")))
        {
            await app.OpenAsync(ct);
            Assert.Equal(1, await CountAsync(app, "platform.setting", ct));
            Assert.Equal(0, await CountAsync(app, "assess.alert", ct));
        }

        await RestoreAsync(name, outcome.DumpPath!, ct);
        await using (var owner = await OpenOwnerAsync(options))
        {
            Assert.NotEmpty(await Migrator(options, time).FindSchemasBehindAsync(owner, ct));
            await AssertSampleRowsAsync(owner, ct);

            time.Advance(TimeSpan.FromMinutes(1));
            var again = await Migrator(options, time).MigrateAsync(owner, ct);
            Assert.True(again.Applied > 0);
            Assert.Empty(await Migrator(options, time).FindSchemasBehindAsync(owner, ct));
            await AssertSampleRowsAsync(owner, ct);
        }
    }

    private static async Task MigrateBeforeCutoffAsync(NpgsqlConnection owner, CancellationToken ct)
    {
        foreach (var database in Modules.Databases)
        {
            await using var context = database.CreateContext(owner);
            var last = context.Database.GetMigrations().LastOrDefault(m => string.CompareOrdinal(m, Cutoff) < 0);
            if (last is null)
            {
                continue;
            }

            await context.GetService<IMigrator>().MigrateAsync(last, ct);
            if (owner.State != System.Data.ConnectionState.Open)
            {
                await owner.OpenAsync(ct);
            }
        }
    }

    private static async Task InsertSampleRowsAsync(NpgsqlConnection owner, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO platform.setting (key, value, updated_at) VALUES ('registration', '"invite"', now());
            INSERT INTO platform.job (kind, payload, run_at, priority, state, attempts, max_attempts, timeout_seconds)
                VALUES ('intel.sync-source', '{}', now(), 5, 'pending', 0, 5, 600);
            INSERT INTO iam.organization (id, name, slug, locale, time_zone, settings, created_at)
                VALUES (uuidv7(), 'Muster AG', 'muster', 'de', 'Europe/Zurich', '{}', now());
            INSERT INTO catalog.unknown_product (raw_key, product, cpe_pairs, record_count, in_kev)
                VALUES ('cpe:acme:widget', 'widget', '{}', 3, false);
            """, owner);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task AssertSampleRowsAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        Assert.Equal(1, await CountAsync(connection, "platform.setting", ct));
        Assert.Equal(1, await CountAsync(connection, "platform.job WHERE kind = 'intel.sync-source'", ct));
        Assert.Equal(1, await CountAsync(connection, "iam.organization WHERE slug = 'muster'", ct));
        Assert.Equal(1, await CountAsync(connection, "catalog.unknown_product WHERE raw_key = 'cpe:acme:widget'", ct));
    }

    private async Task RestoreAsync(string database, string dump, CancellationToken ct)
    {
        NpgsqlConnection.ClearAllPools();
        await using (var admin = new NpgsqlConnection(server.OwnerConnectionString("postgres")))
        {
            await admin.OpenAsync(ct);
            await using var recreate = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE); CREATE DATABASE \"{database}\"", admin);
            await recreate.ExecuteNonQueryAsync(ct);
        }

        var result = await server.Container.ExecAsync(["pg_restore", "--username=" + PostgresServer.OwnerUser, "--dbname=" + database, "--no-owner", dump], ct);
        Assert.True(result.ExitCode == 0, result.Stderr);
    }

    private DatabaseOptions Options(string database) => new()
    {
        Host = server.Host,
        Port = server.Port,
        Database = database,
        OwnerUser = PostgresServer.OwnerUser,
        OwnerPassword = PostgresServer.OwnerPassword,
        AppPassword = "app-test-password",
    };

    private static DatabaseMigrator Migrator(DatabaseOptions options, TimeProvider time) =>
        new(options, Modules.Databases, new ServerSideBackupWriter(options, time, NullLogger<ServerSideBackupWriter>.Instance), NullLogger<DatabaseMigrator>.Instance);

    private static async Task<NpgsqlConnection> OpenOwnerAsync(DatabaseOptions options)
    {
        var connection = new NpgsqlConnection(options.OwnerConnectionString("tests"));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string from, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT count(*) FROM " + from, connection);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task<bool> SchemaExistsAsync(NpgsqlConnection connection, string schema, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = $1)", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = schema });
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    private sealed class NoDump : IBackupWriter
    {
        public Task<string?> WriteAsync(NpgsqlConnection owner, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
