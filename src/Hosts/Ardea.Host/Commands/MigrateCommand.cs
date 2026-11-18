using Ardea.Platform.Persistence;
using Npgsql;

namespace Ardea.Host.Commands;

/// <summary><c>ardea migrate [--roles-only]</c>: runs once per start and exits (master plan 3.1, 12.2).</summary>
internal static partial class MigrateCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var configuration = HostConfiguration.Load();
        var check = HostConfiguration.CheckMigration(configuration);
        if (!check.IsValid)
        {
            return HostConfiguration.Fail(check);
        }

        using var loggers = LoggerFactory.Create(HostConfiguration.AddJsonLogging);
        var logger = loggers.CreateLogger("Ardea.Migrate");
        var options = DatabaseOptions.FromConfiguration(configuration);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(30));

        await using var owner = await ConnectAsync(options, logger, cancellation.Token);
        var migrator = new DatabaseMigrator(
            options,
            Modules.Databases,
            new ServerSideBackupWriter(options, TimeProvider.System, loggers.CreateLogger<ServerSideBackupWriter>()),
            loggers.CreateLogger<DatabaseMigrator>());

        if (args.Contains("--roles-only"))
        {
            await migrator.EnsureAppRoleAsync(owner, cancellation.Token);
            return 0;
        }

        var outcome = await migrator.MigrateAsync(owner, cancellation.Token);
        LogDone(logger, outcome.Applied, outcome.DumpPath ?? "none");
        return 0;
    }

    /// <summary>The database container may still be starting; wait up to a minute.</summary>
    private static async Task<NpgsqlConnection> ConnectAsync(DatabaseOptions options, ILogger logger, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(1);
        while (true)
        {
            var connection = new NpgsqlConnection(options.OwnerConnectionString("ardea-migrate"));
            try
            {
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch (Exception ex) when ((ex is NpgsqlException or System.Net.Sockets.SocketException) && DateTime.UtcNow < deadline)
            {
                await connection.DisposeAsync();
                LogWaiting(logger, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    [LoggerMessage(EventId = 1301, Level = LogLevel.Information, Message = "Waiting for the database: {Reason}")]
    private static partial void LogWaiting(ILogger logger, string reason);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Information, Message = "Migration finished: {Applied} migration(s) applied, dump: {Dump}")]
    private static partial void LogDone(ILogger logger, int applied, string dump);
}
