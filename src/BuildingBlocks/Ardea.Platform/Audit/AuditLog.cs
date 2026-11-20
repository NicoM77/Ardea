using System.Text.Json;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Npgsql;
using NpgsqlTypes;

namespace Ardea.Platform.Audit;

public sealed record AuditEntry(string Action, string? SubjectType = null, string? SubjectId = null, object? Data = null)
{
    public Guid? OrganizationId { get; init; }

    public Guid? ActorUserId { get; init; }

    public bool InstanceEvent { get; init; }
}

public sealed class AuditOrigin
{
    public string Origin { get; set; } = "system";

    public string? IpAddress { get; set; }
}

public interface IAuditLog
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken);
}

internal sealed class AuditLog(IUnitOfWork unitOfWork, ITenantContext tenant, AuditOrigin origin) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        var organization = entry.InstanceEvent ? null : entry.OrganizationId ?? tenant.OrganizationId;
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO platform.audit_event (organization_id, actor_user_id, action, subject_type, subject_id, data, origin, ip_address)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)organization ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)(entry.ActorUserId ?? tenant.UserId) ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid });
        command.Parameters.Add(new NpgsqlParameter { Value = entry.Action });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)entry.SubjectType ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Varchar });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)entry.SubjectId ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Varchar });
        command.Parameters.Add(new NpgsqlParameter
        {
            Value = entry.Data is null ? DBNull.Value : JsonSerializer.SerializeToDocument(entry.Data, Json),
            NpgsqlDbType = NpgsqlDbType.Jsonb,
        });
        command.Parameters.Add(new NpgsqlParameter { Value = origin.Origin });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)origin.IpAddress ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Varchar });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
