using System.Security.Cryptography;
using System.Text;
using Ardea.Platform.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ardea.Iam.Security;

internal static class SetupToken
{
    public const string ConfigurationKey = "SETUP_TOKEN";

    public static readonly SettingDefinition Setting = new("iam.setup_token", ConfigurationKey, IsSecret: true);

    public static bool Matches(string? expected, string? provided)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(provided)));
    }

    public static async Task<bool> AnyUserAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT iam.any_user()", connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}

internal sealed partial class SetupTokenAnnouncer(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<SetupTokenAnnouncer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(configuration[SetupToken.ConfigurationKey]))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<Platform.Persistence.IUnitOfWork>();
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        if (await SetupToken.AnyUserAsync(connection, cancellationToken))
        {
            return;
        }

        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('ardea-setup-token'))", connection))
        {
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var settings = scope.ServiceProvider.GetRequiredService<IInstanceSettings>();
        var existing = await settings.GetAsync(SetupToken.Setting, cancellationToken);
        var token = existing.Value;
        if (token is null)
        {
            token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');
            await settings.SetAsync(SetupToken.Setting, token, null, cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        LogToken(token);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
        Message = "No user exists yet. Open the instance and create the first user with this setup token: {SetupToken}")]
    private partial void LogToken(string setupToken);
}
