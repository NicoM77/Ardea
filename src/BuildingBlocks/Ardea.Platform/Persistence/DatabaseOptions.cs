using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Ardea.Platform.Persistence;

public sealed record DatabaseOptions
{
    public const string OwnerPasswordKey = "DB_OWNER_PASSWORD";
    public const string AppPasswordKey = "DB_APP_PASSWORD";

    public string Host { get; init; } = "db";

    public int Port { get; init; } = 5432;

    public string Database { get; init; } = "ardea";

    public string OwnerUser { get; init; } = "ardea_migrator";

    public string? OwnerPassword { get; init; }

    public string AppUser { get; init; } = "ardea_app";

    public string? AppPassword { get; init; }

    public static DatabaseOptions FromConfiguration(IConfiguration configuration) => new()
    {
        Host = configuration["DB_HOST"] ?? "db",
        Port = int.TryParse(configuration["DB_PORT"], out var port) ? port : 5432,
        Database = configuration["DB_NAME"] ?? "ardea",
        OwnerUser = configuration["DB_OWNER_USER"] ?? "ardea_migrator",
        OwnerPassword = configuration[OwnerPasswordKey],
        AppUser = configuration["DB_APP_USER"] ?? "ardea_app",
        AppPassword = configuration[AppPasswordKey],
    };

    public string AppConnectionString(string applicationName) => Build(AppUser, AppPassword, applicationName);

    public string OwnerConnectionString(string applicationName) => Build(OwnerUser, OwnerPassword, applicationName);

    private string Build(string user, string? password, string applicationName) => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Port,
        Database = Database,
        Username = user,
        Password = password,
        ApplicationName = applicationName,
        NoResetOnClose = false,
        GssEncryptionMode = GssEncryptionMode.Disable,
    }.ConnectionString;
}
