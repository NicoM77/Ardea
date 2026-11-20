using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ardea.Platform.Persistence;

public sealed partial class DatabaseMigrator(
    DatabaseOptions options,
    IReadOnlyList<IModuleDatabase> databases,
    IBackupWriter backup,
    ILogger<DatabaseMigrator> logger,
    MasterDataOptions? masterData = null)
{
    private readonly MasterDataOptions _masterData = masterData ?? new MasterDataOptions(MasterDataOptions.FindDefault());

    public async Task EnsureAppRoleAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.AppPassword))
        {
            throw new InvalidOperationException("The application role needs a password (ARDEA_DB_APP_PASSWORD).");
        }

        for (var attempt = 0; ; attempt++)
        {
            await EnsureOpenAsync(owner, cancellationToken);
            try
            {
                await using var command = new NpgsqlCommand(
                    """
                    SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname = $1)
                        THEN format('ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD %L', $1, $2)
                        ELSE format('CREATE ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD %L', $1, $2)
                    END
                    """, owner);
                command.Parameters.Add(new NpgsqlParameter { Value = options.AppUser });
                command.Parameters.Add(new NpgsqlParameter { Value = options.AppPassword });
                var ddl = (string)(await command.ExecuteScalarAsync(cancellationToken))!;
                await using var apply = new NpgsqlCommand(ddl, owner);
                await apply.ExecuteNonQueryAsync(cancellationToken);
                break;
            }
            catch (PostgresException ex) when (attempt < 10 && IsConcurrentRoleChange(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(20, 100) * (attempt + 1)), cancellationToken);
            }
        }

        await using var connect = new NpgsqlCommand(
            $"GRANT CONNECT ON DATABASE {Quote(options.Database)} TO {Quote(options.AppUser)}", owner);
        await connect.ExecuteNonQueryAsync(cancellationToken);
        LogAppRoleReady(options.AppUser);
    }

    public async Task<MigrationOutcome> MigrateAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        await using var lockSession = new NpgsqlConnection(options.OwnerConnectionString("ardea-migrate-lock"));
        await lockSession.OpenAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_lock(hashtext('ardea-migrate'))", lockSession))
        {
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return await MigrateLockedAsync(owner, cancellationToken);
    }

    private async Task<MigrationOutcome> MigrateLockedAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        await EnsureAppRoleAsync(owner, cancellationToken);

        var pending = new List<(IModuleDatabase Database, IReadOnlyList<string> Migrations)>();
        foreach (var database in databases)
        {
            await using var context = database.CreateContext(owner);
            var migrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            await EnsureOpenAsync(owner, cancellationToken);
            if (migrations.Count > 0)
            {
                pending.Add((database, migrations));
            }
        }

        string? dump = null;
        if (pending.Count > 0 && await HasApplicationDataAsync(owner, cancellationToken))
        {
            dump = await backup.WriteAsync(owner, cancellationToken);
        }

        foreach (var (database, migrations) in pending)
        {
            await using var context = database.CreateContext(owner);
            await context.Database.MigrateAsync(cancellationToken);
            await EnsureOpenAsync(owner, cancellationToken);
            LogSchemaMigrated(database.Schema, migrations.Count);
        }

        foreach (var database in databases)
        {
            var changed = await database.SeedAsync(owner, _masterData, cancellationToken);
            await EnsureOpenAsync(owner, cancellationToken);
            if (changed > 0)
            {
                LogMasterDataLoaded(database.Schema, changed);
            }
        }

        foreach (var database in databases)
        {
            await GrantAsync(owner, database, cancellationToken);
        }

        return new MigrationOutcome(pending.Sum(p => p.Migrations.Count), dump);
    }

    public async Task<IReadOnlyList<string>> FindSchemasBehindAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var behind = new List<string>();
        foreach (var database in databases)
        {
            await using var context = database.CreateContext(connection);
            if ((await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                behind.Add(database.Schema);
            }
        }

        return behind;
    }

    private static bool IsConcurrentRoleChange(PostgresException exception) =>
        exception.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DuplicateObject
        || (exception.SqlState == PostgresErrorCodes.InternalError && exception.MessageText.Contains("concurrently updated", StringComparison.Ordinal));

    private static async Task EnsureOpenAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    private static async Task<bool> HasApplicationDataAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'platform')", owner);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task GrantAsync(NpgsqlConnection owner, IModuleDatabase database, CancellationToken cancellationToken)
    {
        var schema = Quote(database.Schema);
        var app = Quote(options.AppUser);
        var statements = new List<string>
        {
            $"GRANT USAGE ON SCHEMA {schema} TO {app}",
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schema} TO {app}",
            $"GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA {schema} TO {app}",
            $"REVOKE INSERT, UPDATE, DELETE ON {schema}.{Quote(DbContextOptionsExtensions.MigrationsHistoryTable)} FROM {app}",
        };
        statements.AddRange(database.AppendOnlyTables.Select(t => $"REVOKE UPDATE, DELETE, TRUNCATE ON {schema}.{Quote(t)} FROM {app}"));

        foreach (var sql in statements)
        {
            await using var command = new NpgsqlCommand(sql, owner);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Application role {Role} is ready")]
    private partial void LogAppRoleReady(string role);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Schema {Schema}: applied {Count} migration(s)")]
    private partial void LogSchemaMigrated(string schema, int count);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Schema {Schema}: master data loaded, {Count} row(s) changed")]
    private partial void LogMasterDataLoaded(string schema, int count);
}

public sealed record MigrationOutcome(int Applied, string? DumpPath);
