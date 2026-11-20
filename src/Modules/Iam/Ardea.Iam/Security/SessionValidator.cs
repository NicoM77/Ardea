using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ardea.Iam.Security;

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
        await using var command = dataSource.CreateCommand("SELECT iam.validate_session($1, $2, $3, $4)");
        command.Parameters.Add(new NpgsqlParameter { Value = sessionId });
        command.Parameters.Add(new NpgsqlParameter { Value = userId });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)stamp ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter { Value = Domain.UserSession.IdleTimeout });
        var valid = (bool)(await command.ExecuteScalarAsync(context.HttpContext.RequestAborted))!;
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
