using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ardea.Platform.Persistence;

/// <summary>Writes a dump before a migration (master plan 12.2, ADR 024).</summary>
public interface IBackupWriter
{
    /// <summary>Returns the path of the dump on the database server, or <c>null</c> when none was written.</summary>
    Task<string?> WriteAsync(NpgsqlConnection owner, CancellationToken cancellationToken);
}

/// <summary>
/// The application image contains no <c>pg_dump</c>. The database server writes the dump itself with
/// <c>COPY ... TO PROGRAM</c>, using the <c>pg_dump</c> of the database image, into <c>backup/</c> next
/// to its data directory; the newest three dumps are kept. This needs a role that may run server
/// programs, which the bundled database's owner role is. With any other database the step is skipped
/// with a clear warning, and backups are the operator's job.
/// </summary>
public sealed partial class ServerSideBackupWriter(DatabaseOptions options, TimeProvider time, ILogger<ServerSideBackupWriter> logger) : IBackupWriter
{
    public const string Directory = "/var/lib/postgresql/backup";
    private const int Keep = 3;

    public async Task<string?> WriteAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        if (!await MayRunServerProgramsAsync(owner, cancellationToken))
        {
            LogSkipped(options.OwnerUser);
            return null;
        }

        var file = $"{Directory}/ardea-{time.GetUtcNow():yyyyMMdd'T'HHmmss'Z'}.dump";
        var database = ShellQuote(options.Database);
        var user = ShellQuote(options.OwnerUser);
        var program =
            $"mkdir -p {Directory} && pg_dump --format=custom --username={user} --dbname={database} --file={ShellQuote(file)} " +
            $"&& cd {Directory} && ls -1t ardea-*.dump | tail -n +{Keep + 1} | xargs -r rm --";

        await using var command = new NpgsqlCommand(
            $"COPY (SELECT 1) TO PROGRAM {SqlLiteral(program)}", owner);
        command.CommandTimeout = 0;
        await command.ExecuteNonQueryAsync(cancellationToken);
        LogWritten(file);
        return file;
    }

    private static async Task<bool> MayRunServerProgramsAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT rolsuper OR pg_has_role(current_user, 'pg_execute_server_program', 'MEMBER')
            FROM pg_roles WHERE rolname = current_user
            """, owner);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string SqlLiteral(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning,
        Message = "No dump before migration: role {Role} may not run server programs. Back up the database yourself before updating.")]
    private partial void LogSkipped(string role);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Information, Message = "Dump before migration written on the database server: {File}")]
    private partial void LogWritten(string file);
}
