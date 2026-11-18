using System.Text.Json;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Npgsql;
using NpgsqlTypes;

namespace Ardea.Platform.Audit;

/// <summary>
/// One audit event (master plan 9.4): sign-ins, role changes, changes to profile, rules and settings,
/// status changes. <paramref name="Data"/> holds identifiers and changed values, never secrets.
/// </summary>
public sealed record AuditEntry(string Action, string? SubjectType = null, string? SubjectId = null, object? Data = null)
{
    /// <summary>Overrides the tenant context, e.g. for a sign-in before an organisation is chosen. Empty for instance events.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>Overrides the acting user, e.g. for a failed sign-in of a known account.</summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>Marks an instance event even inside an organisation's unit of work.</summary>
    public bool InstanceEvent { get; init; }
}

/// <summary>Where the current unit of work comes from: set by the API per request, by the worker per job.</summary>
public sealed class AuditOrigin
{
    public string Origin { get; set; } = "system";

    public string? IpAddress { get; set; }
}

/// <summary>Writes audit events in the unit of work: they commit with the change they record.</summary>
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
