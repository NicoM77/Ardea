using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ardea.TestSupport;

/// <summary>
/// One PostgreSQL 18 server per test run. Tests that need an empty database create their own from
/// it in milliseconds (<see cref="CreateDatabaseAsync"/>) instead of starting another container.
/// The owner role mirrors the bundled compose setup: <c>ardea_migrator</c> as the image's superuser.
/// </summary>
public sealed class PostgresServer : IAsyncLifetime
{
    public const string Image = "postgres:18";
    public const string OwnerUser = "ardea_migrator";
    public const string OwnerPassword = "owner-test-password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithUsername(OwnerUser)
        .WithPassword(OwnerPassword)
        .WithDatabase("postgres_admin")
        .Build();

    private int _counter;

    public string Host => _container.Hostname;

    public int Port => _container.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort);

    public PostgreSqlContainer Container => _container;

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates a fresh, empty database and returns its name.</summary>
    public async Task<string> CreateDatabaseAsync(string prefix = "ardea")
    {
        var name = $"{prefix}_{Interlocked.Increment(ref _counter)}_{Guid.NewGuid():N}"[..40];
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        return name;
    }

    public string OwnerConnectionString(string database) => new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
    {
        Database = database,
    }.ConnectionString;
}
