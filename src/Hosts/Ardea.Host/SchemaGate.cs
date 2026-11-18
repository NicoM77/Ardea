using Ardea.Platform.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Ardea.Host;

/// <summary>API and worker start only on a schema at least as new as their build (master plan 4.11).</summary>
internal static class SchemaGate
{
    public static async Task<IReadOnlyList<string>> FindBehindAsync(NpgsqlDataSource dataSource, DatabaseOptions options, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var migrator = new DatabaseMigrator(options, Modules.Databases, NoBackup.Instance, NullLogger<DatabaseMigrator>.Instance);
        return await migrator.FindSchemasBehindAsync(connection, cancellationToken);
    }

    private sealed class NoBackup : IBackupWriter
    {
        public static readonly NoBackup Instance = new();

        public Task<string?> WriteAsync(NpgsqlConnection owner, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
