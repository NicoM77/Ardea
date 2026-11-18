using System.Text.Json;
using Ardea.Platform.Configuration;
using Ardea.Platform.Persistence;
using Ardea.Platform.Secrets;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace Ardea.Platform.Settings;

/// <summary>
/// A setting of the instance that may be changed at runtime (SMTP, AI providers, sources, limits).
/// A set environment variable takes precedence and locks the field (master plan 12.4).
/// </summary>
public sealed record SettingDefinition(string Key, string? ConfigurationKey = null, bool IsSecret = false, string? Default = null);

public enum SettingSource
{
    Default,
    Database,
    Environment,
}

/// <summary>The effective value. For secrets, <see cref="Value"/> is the plaintext for internal use only.</summary>
public sealed record SettingValue(string Key, string? Value, SettingSource Source)
{
    /// <summary>Set by an environment variable: the UI shows it read-only with a hint.</summary>
    public bool Locked => Source == SettingSource.Environment;
}

public interface IInstanceSettings
{
    Task<SettingValue> GetAsync(SettingDefinition definition, CancellationToken cancellationToken);

    /// <summary>Stores a value in the unit of work; <c>null</c> removes it. Fails for a locked setting.</summary>
    Task SetAsync(SettingDefinition definition, string? value, Guid? changedBy, CancellationToken cancellationToken);
}

internal sealed class InstanceSettings(IUnitOfWork unitOfWork, IConfiguration configuration, ISecretProtector secrets, TimeProvider time) : IInstanceSettings
{
    public async Task<SettingValue> GetAsync(SettingDefinition definition, CancellationToken cancellationToken)
    {
        if (definition.ConfigurationKey is { } key && configuration[key] is { Length: > 0 } fromEnvironment)
        {
            return new SettingValue(definition.Key, fromEnvironment, SettingSource.Environment);
        }

        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT value, secret_value FROM platform.setting WHERE key = $1", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = definition.Key });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var stored = definition.IsSecret
                ? reader.IsDBNull(1) ? null : secrets.Unprotect(Purpose(definition), reader.GetString(1))
                : reader.IsDBNull(0) ? null : reader.GetFieldValue<JsonDocument>(0).RootElement.GetString();
            if (stored is not null)
            {
                return new SettingValue(definition.Key, stored, SettingSource.Database);
            }
        }

        return new SettingValue(definition.Key, definition.Default, SettingSource.Default);
    }

    public async Task SetAsync(SettingDefinition definition, string? value, Guid? changedBy, CancellationToken cancellationToken)
    {
        if (definition.ConfigurationKey is { } key && configuration[key] is { Length: > 0 })
        {
            throw new InvalidOperationException($"{definition.Key} is set by {ArdeaConfiguration.VariableName(key)} and cannot be changed here.");
        }

        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        if (value is null)
        {
            await using var delete = new NpgsqlCommand("DELETE FROM platform.setting WHERE key = $1", connection);
            delete.Parameters.Add(new NpgsqlParameter { Value = definition.Key });
            await delete.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        await using var upsert = new NpgsqlCommand(
            """
            INSERT INTO platform.setting (key, value, secret_value, updated_at, updated_by) VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (key) DO UPDATE SET value = excluded.value, secret_value = excluded.secret_value,
                updated_at = excluded.updated_at, updated_by = excluded.updated_by
            """, connection);
        upsert.Parameters.Add(new NpgsqlParameter { Value = definition.Key });
        upsert.Parameters.Add(new NpgsqlParameter
        {
            Value = definition.IsSecret ? DBNull.Value : JsonSerializer.SerializeToDocument(value),
            NpgsqlDbType = NpgsqlDbType.Jsonb,
        });
        upsert.Parameters.Add(new NpgsqlParameter
        {
            Value = definition.IsSecret ? secrets.Protect(Purpose(definition), value) : DBNull.Value,
            NpgsqlDbType = NpgsqlDbType.Text,
        });
        upsert.Parameters.Add(new NpgsqlParameter { Value = time.GetUtcNow() });
        upsert.Parameters.Add(new NpgsqlParameter { Value = (object?)changedBy ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid });
        await upsert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Purpose(SettingDefinition definition) => $"setting:{definition.Key}";
}
