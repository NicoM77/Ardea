using Ardea.Platform.Persistence;
using Ardea.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Ardea.Platform.Tests.Persistence;

/// <summary>A fresh database on the shared server with options for both roles.</summary>
internal sealed class MigrationDatabase(PostgresServer server, string name)
{
    public const string AppPassword = "app-test-password";

    public DatabaseOptions Options { get; } = new()
    {
        Host = server.Host,
        Port = server.Port,
        Database = name,
        OwnerUser = PostgresServer.OwnerUser,
        OwnerPassword = PostgresServer.OwnerPassword,
        AppPassword = AppPassword,
    };

    public static async Task<MigrationDatabase> CreateAsync(PostgresServer server) =>
        new(server, await server.CreateDatabaseAsync());

    public DatabaseMigrator Migrator(IBackupWriter? backup = null) =>
        new(Options, [PlatformDatabase.Instance], backup ?? Backup(), NullLogger<DatabaseMigrator>.Instance);

    public ServerSideBackupWriter Backup() =>
        new(Options, new FakeTimeProvider(DateTimeOffset.UtcNow), NullLogger<ServerSideBackupWriter>.Instance);

    public async Task<NpgsqlConnection> OpenOwnerAsync()
    {
        var connection = new NpgsqlConnection(Options.OwnerConnectionString("tests"));
        await connection.OpenAsync();
        return connection;
    }

    public async Task<NpgsqlConnection> OpenAppAsync()
    {
        var connection = new NpgsqlConnection(Options.AppConnectionString("tests"));
        await connection.OpenAsync();
        return connection;
    }
}
