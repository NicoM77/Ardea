using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ardea.Platform.Persistence;

/// <summary>
/// What <c>ardea migrate</c> does (master plan 4.11, 12.2): create the application role, take a dump
/// through the database server when migrations are pending on a database with data, apply every
/// schema's migrations in fixed order, and grant the application role its rights.
/// </summary>
public sealed partial class DatabaseMigrator(DatabaseOptions options, IReadOnlyList<IModuleDatabase> databases, IBackupWriter backup, ILogger<DatabaseMigrator> logger)
{
    public async Task EnsureAppRoleAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.AppPassword))
        {
            throw new InvalidOperationException("The application role needs a password (ARDEA_DB_APP_PASSWORD).");
        }

        // Role names and passwords cannot be bound as parameters in DDL; format() quotes them safely.
        // Roles are global to the server: a concurrent migrate on another database may create the
        // role between our check and our CREATE, so a duplicate is retried as ALTER.
        for (var attempt = 0; ; attempt++)
        {
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
            catch (PostgresException ex) when (attempt == 0 && ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DuplicateObject)
            {
            }
        }

        await using var connect = new NpgsqlCommand(
            $"GRANT CONNECT ON DATABASE {Quote(options.Database)} TO {Quote(options.AppUser)}", owner);
        await connect.ExecuteNonQueryAsync(cancellationToken);
        LogAppRoleReady(options.AppUser);
    }

    public async Task<MigrationOutcome> MigrateAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        // Two migrate processes against the same database run one after the other.
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_lock(hashtext('ardea-migrate'))", owner))
        {
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            return await MigrateLockedAsync(owner, cancellationToken);
        }
        finally
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtext('ardea-migrate'))", owner);
            await unlock.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private async Task<MigrationOutcome> MigrateLockedAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        await EnsureAppRoleAsync(owner, cancellationToken);

        var pending = new List<(IModuleDatabase Database, IReadOnlyList<string> Migrations)>();
        foreach (var database in databases)
        {
            await using var context = database.CreateContext(owner);
            var migrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
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
            LogSchemaMigrated(database.Schema, migrations.Count);
        }

        foreach (var database in databases)
        {
            await GrantAsync(owner, database, cancellationToken);
        }

        return new MigrationOutcome(pending.Sum(p => p.Migrations.Count), dump);
    }

    /// <summary>
    /// Whether the current role sees a schema at least as new as this build (master plan 4.11).
    /// API and worker refuse to start otherwise; a newer, compatibly extended schema is fine.
    /// </summary>
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
}

public sealed record MigrationOutcome(int Applied, string? DumpPath);
