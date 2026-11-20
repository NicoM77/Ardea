using System.Globalization;
using System.Text;
using System.Text.Json;
using Ardea.Iam.Persistence;
using Ardea.Platform.Http;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Ardea.Iam.Features;

internal sealed record AuditEventView(
    Guid Id, DateTimeOffset At, string Action, string? SubjectType, string? SubjectId, JsonElement? Data, string Origin, string ActorKind, string? ActorName);

internal sealed record AuditPage(IReadOnlyList<AuditEventView> Items, string? NextCursor);

internal static class AuditEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    public static void Map(IEndpointRouteBuilder api)
    {
        var org = api.MapOrganization().WithTags("Organizations");
        org.MapGet("/audit", ListAsync).WithName("listAuditEvents").RequireOrganizationRole(OrganizationRole.Admin)
            .Produces<AuditPage>().Problems(400, 403, 404);
    }

    private static async Task<IResult> ListAsync(
        int? limit, string? cursor, HttpContext http, IUnitOfWork unitOfWork, IamDbContext db, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        (DateTimeOffset At, Guid Id)? after = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (Decode(cursor) is not { } position)
            {
                return IamErrors.Validation(new() { ["cursor"] = ["This cursor is not valid; start from the first page."] });
            }

            after = position;
        }

        var organizationId = http.OrganizationAccess().OrganizationId;
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, at, action, subject_type, subject_id, data, origin, actor_user_id
            FROM platform.audit_event
            WHERE organization_id = $1 AND ($2::timestamptz IS NULL OR (at, id) < ($2, $3))
            ORDER BY at DESC, id DESC
            LIMIT $4
            """, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = organizationId, NpgsqlDbType = NpgsqlDbType.Uuid });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)after?.At ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.TimestampTz });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)after?.Id ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid });
        command.Parameters.Add(new NpgsqlParameter { Value = size + 1 });

        var rows = new List<(Guid Id, DateTimeOffset At, string Action, string? SubjectType, string? SubjectId, JsonElement? Data, string Origin, Guid? Actor)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                JsonElement? data = null;
                if (!await reader.IsDBNullAsync(5, cancellationToken))
                {
                    using var document = JsonDocument.Parse(reader.GetString(5));
                    data = document.RootElement.Clone();
                }

                rows.Add((
                    reader.GetGuid(0),
                    reader.GetFieldValue<DateTimeOffset>(1),
                    reader.GetString(2),
                    await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetString(3),
                    await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4),
                    data,
                    reader.GetString(6),
                    await reader.IsDBNullAsync(7, cancellationToken) ? null : reader.GetGuid(7)));
            }
        }

        var page = rows.Take(size).ToList();

        var actorIds = page.Where(r => r.Actor is not null).Select(r => r.Actor!.Value).Distinct().ToList();
        var names = await db.Memberships.Where(m => actorIds.Contains(m.UserId))
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        var items = page.Select(r =>
        {
            var (kind, name) = r.Actor is not { } actor ? ("system", (string?)null)
                : names.TryGetValue(actor, out var displayName) ? ("member", displayName)
                : ("former_member", null);
            return new AuditEventView(r.Id, r.At, r.Action, r.SubjectType, r.SubjectId, r.Data, r.Origin, kind, name);
        }).ToList();

        var next = rows.Count > size ? Encode(page[^1].At, page[^1].Id) : null;
        return Results.Ok(new AuditPage(items, next));
    }

    internal static string Encode(DateTimeOffset at, Guid id)
    {
        var micros = (at.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10;
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(micros.ToString(CultureInfo.InvariantCulture) + "_" + id.ToString("N")));
    }

    internal static (DateTimeOffset At, Guid Id)? Decode(string cursor)
    {
        try
        {
            var text = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
            var parts = text.Split('_');
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var micros)
                && Guid.TryParseExact(parts[1], "N", out var id))
            {
                return (DateTimeOffset.UnixEpoch.AddTicks(micros * 10), id);
            }
        }
        catch (FormatException)
        {
        }

        return null;
    }
}
