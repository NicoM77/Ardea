using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ardea.Iam.Security;

/// <summary>
/// Checks the server-side session behind every authenticated request: it exists, is neither
/// revoked nor past its absolute lifetime, and the account's security stamp still matches, so a
/// password change, an MFA change or "sign out everywhere" ends it at once (master plan 9.3).
/// Uses its own short command: this runs before the request's unit of work and its tenant exist.
/// </summary>
internal static class SessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirstValue(SessionClaims.SessionId), out var sessionId)
            || !Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            Reject(context);
            return;
        }

        var stampClaimType = context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>()
            .Value.ClaimsIdentity.SecurityStampClaimType;
        var stamp = principal!.FindFirstValue(stampClaimType);
        var dataSource = context.HttpContext.RequestServices.GetRequiredService<NpgsqlDataSource>();
        await using var command = dataSource.CreateCommand(
            """
            WITH valid AS (
                SELECT s.id FROM iam.session s JOIN iam."user" u ON u.id = s.user_id
                WHERE s.id = $1 AND s.user_id = $2 AND s.revoked_at IS NULL AND s.expires_at > now()
                  AND s.last_seen_at > now() - $4 AND u.security_stamp IS NOT DISTINCT FROM $3
            ), touched AS (
                UPDATE iam.session SET last_seen_at = now()
                WHERE id IN (SELECT id FROM valid) AND last_seen_at < now() - interval '5 minutes'
                RETURNING id
            )
            SELECT count(*) FROM valid
            """);
        command.Parameters.Add(new NpgsqlParameter { Value = sessionId });
        command.Parameters.Add(new NpgsqlParameter { Value = userId });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)stamp ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter { Value = Domain.UserSession.IdleTimeout });
        var valid = (long)(await command.ExecuteScalarAsync(context.HttpContext.RequestAborted))! == 1;
        if (!valid)
        {
            Reject(context);
        }
    }

    private static void Reject(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        context.ShouldRenew = false;
    }
}
