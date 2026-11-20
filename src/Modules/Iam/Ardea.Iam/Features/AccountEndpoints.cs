using System.Globalization;
using System.Text;
using Ardea.Iam.Domain;
using Ardea.Iam.Persistence;
using Ardea.Iam.Security;
using Ardea.Platform.Audit;
using Ardea.Platform.Http;
using Ardea.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Iam.Features;

internal static class AccountEndpoints
{
    public const int RecoveryCodeCount = 10;

    public static void Map(IEndpointRouteBuilder api)
    {
        var account = api.MapGroup("/account").WithTags("Account").RequireAuthorization();
        account.MapPatch("", UpdateAsync).WithName("updateAccount").NoContent().Problems(400);
        account.MapPost("/password", ChangePasswordAsync).WithName("changePassword").NoContent().Problems(400).RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        account.MapPost("/mfa/setup", SetupMfaAsync).WithName("startMfaSetup").RequireRateLimiting(AuthEndpoints.RateLimitPolicy).Produces<MfaSetupResponse>().Problems(400, 409);
        account.MapPost("/mfa", EnableMfaAsync).WithName("enableMfa").Produces<RecoveryCodesResponse>().Problems(400).RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        account.MapDelete("/mfa", DisableMfaAsync).WithName("disableMfa").NoContent().Problems(400).RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        account.MapGet("/sessions", ListSessionsAsync).WithName("listSessions");
        account.MapDelete("/sessions/{sessionId:guid}", RevokeSessionAsync).WithName("revokeSession").NoContent().Problems(404);
        account.MapDelete("/sessions", RevokeOtherSessionsAsync).WithName("revokeOtherSessions").NoContent();
    }

    private static async Task<IResult> UpdateAsync(
        UpdateAccountRequest request, HttpContext http, IUnitOfWork unitOfWork, UserManager<ArdeaUser> users, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors().Required("displayName", request.DisplayName, "Enter your name.").MaxLength("displayName", request.DisplayName, 200);
        if (errors.Any)
        {
            return IamErrors.Validation(errors.Errors);
        }

        var user = await CurrentUserAsync(http, users);
        user.DisplayName = request.DisplayName!.Trim();
        await users.UpdateAsync(user);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request, HttpContext http, IUnitOfWork unitOfWork, UserManager<ArdeaUser> users, ArdeaSignInManager signIn,
        IamDbContext db, IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(http, users);
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword ?? string.Empty, request.NewPassword ?? string.Empty);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "PasswordMismatch"))
            {
                await users.AccessFailedAsync(user);
                await unitOfWork.CommitAsync(cancellationToken);
                return IamErrors.Validation(new() { ["currentPassword"] = ["The current password is wrong."] });
            }

            return IamErrors.WeakPassword(result, "newPassword");
        }

        await ReissueAsync(http, user, signIn, db, time, cancellationToken);
        await audit.RecordAsync(new AuditEntry("account.password_changed", "user", user.Id.ToString()) { InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SetupMfaAsync(
        MfaSetupRequest? request, HttpContext http, IUnitOfWork unitOfWork, UserManager<ArdeaUser> users, ArdeaSignInManager signIn, IamDbContext db,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(http, users);
        if (!await CheckPasswordAsync(users, user, request?.Password, unitOfWork, cancellationToken))
        {
            return IamErrors.WrongPassword();
        }

        if (user.TwoFactorEnabled)
        {
            return Platform.Http.ApiProblems.Problem("mfa_enabled", StatusCodes.Status409Conflict, "Two-factor authentication is already on.");
        }

        await users.ResetAuthenticatorKeyAsync(user);
        var key = (await users.GetAuthenticatorKeyAsync(user))!;
        await ReissueAsync(http, user, signIn, db, time, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(new MfaSetupResponse(FormatKey(key), AuthenticatorUri(user.Email!, key)));
    }

    private static async Task<IResult> EnableMfaAsync(
        EnableMfaRequest request, HttpContext http, IUnitOfWork unitOfWork, UserManager<ArdeaUser> users, ArdeaSignInManager signIn,
        IamDbContext db, IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        await AccountLookup.LockAsync(unitOfWork, UserId(http), cancellationToken);
        var user = await CurrentUserAsync(http, users);
        var code = Normalize(request.Code);
        if (code.Length == 0 || !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
        {
            await users.AccessFailedAsync(user);
            await unitOfWork.CommitAsync(cancellationToken);
            return IamErrors.InvalidCode();
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))!.ToList();
        await ReissueAsync(http, user, signIn, db, time, cancellationToken);
        await audit.RecordAsync(new AuditEntry("account.mfa_enabled", "user", user.Id.ToString()) { InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(new RecoveryCodesResponse(codes));
    }

    private static async Task<IResult> DisableMfaAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] DisableMfaRequest request, HttpContext http, IUnitOfWork unitOfWork, UserManager<ArdeaUser> users, ArdeaSignInManager signIn,
        IamDbContext db, IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        await AccountLookup.LockAsync(unitOfWork, UserId(http), cancellationToken);
        var user = await CurrentUserAsync(http, users);
        if (!await CheckPasswordAsync(users, user, request.Password, unitOfWork, cancellationToken))
        {
            return IamErrors.WrongPassword();
        }

        var code = Normalize(request.Code);
        var valid = code.Length > 0 && (await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code)
            || (await users.RedeemTwoFactorRecoveryCodeAsync(user, request.Code!.Trim())).Succeeded);
        if (!valid)
        {
            await users.AccessFailedAsync(user);
            await unitOfWork.CommitAsync(cancellationToken);
            return IamErrors.InvalidCode();
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await ReissueAsync(http, user, signIn, db, time, cancellationToken);
        await audit.RecordAsync(new AuditEntry("account.mfa_disabled", "user", user.Id.ToString()) { InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(HttpContext http, IamDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var userId = UserId(http);
        var current = AuthEndpoints.CurrentSessionId(http.User);
        var now = time.GetUtcNow();
        var idleSince = now - UserSession.IdleTimeout;
        var sessions = await db.Sessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now && s.LastSeenAt > idleSince)
            .OrderByDescending(s => s.LastSeenAt)
            .ToListAsync(cancellationToken);
        return [.. sessions.Select(s => new SessionInfo(s.Id, s.CreatedAt, s.LastSeenAt, s.UserAgent, s.IpAddress, s.Id == current))];
    }

    private static async Task<IResult> RevokeSessionAsync(
        Guid sessionId, HttpContext http, IUnitOfWork unitOfWork, IamDbContext db, IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        var userId = UserId(http);
        var revoked = await db.Sessions
            .Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, time.GetUtcNow()), cancellationToken);
        if (revoked == 0)
        {
            return Platform.Http.ApiProblems.Problem("not_found", StatusCodes.Status404NotFound, "No such session.");
        }

        await audit.RecordAsync(new AuditEntry("account.session_revoked", "session", sessionId.ToString()) { InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeOtherSessionsAsync(
        HttpContext http, IUnitOfWork unitOfWork, IamDbContext db, IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        var count = await AuthEndpoints.RevokeSessionsAsync(db, UserId(http), AuthEndpoints.CurrentSessionId(http.User), time, cancellationToken);
        await audit.RecordAsync(new AuditEntry("account.sessions_revoked", "user", UserId(http).ToString(), new { count }) { InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task ReissueAsync(HttpContext http, ArdeaUser user, ArdeaSignInManager signIn, IamDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        await AuthEndpoints.RevokeSessionsAsync(db, user.Id, except: null, time, cancellationToken);
        await signIn.SignInAsync(user, isPersistent: true);
        _ = http;
    }

    private static async Task<bool> CheckPasswordAsync(UserManager<ArdeaUser> users, ArdeaUser user, string? password, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        if (!await users.IsLockedOutAsync(user) && !string.IsNullOrEmpty(password) && await users.CheckPasswordAsync(user, password))
        {
            return true;
        }

        await users.AccessFailedAsync(user);
        await unitOfWork.CommitAsync(cancellationToken);
        return false;
    }

    private static Guid UserId(HttpContext http) =>
        Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    private static async Task<ArdeaUser> CurrentUserAsync(HttpContext http, UserManager<ArdeaUser> users) =>
        await users.GetUserAsync(http.User) ?? throw new InvalidOperationException("Authenticated request without its account.");

    private static string Normalize(string? code) =>
        (code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);

    internal static string FormatKey(string key)
    {
        var result = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0)
            {
                result.Append(' ');
            }

            result.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return result.ToString().ToUpperInvariant();
    }

    internal static string AuthenticatorUri(string email, string key) => string.Format(
        CultureInfo.InvariantCulture,
        "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
        Uri.EscapeDataString("Ardea"),
        Uri.EscapeDataString(email),
        key);
}
