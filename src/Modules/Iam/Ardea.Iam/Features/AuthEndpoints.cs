using System.Security.Claims;
using Ardea.Iam.Domain;
using Ardea.Iam.Persistence;
using Ardea.Iam.Security;
using Ardea.Platform.Audit;
using Ardea.Platform.Persistence;
using Ardea.Platform.Settings;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Ardea.Iam.Features;

/// <summary>Setup, sign-in, registration and password reset (master plan 9.1, 9.3, 10.2).</summary>
internal static class AuthEndpoints
{
    public const string RateLimitPolicy = "ardea-auth";

    /// <summary>A hash to verify against when the account does not exist, so both cases take as long.</summary>
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<ArdeaUser>().HashPassword(new ArdeaUser { DisplayName = "-" }, Guid.NewGuid().ToString()));

    public static void Map(IEndpointRouteBuilder api)
    {
        var setup = api.MapGroup("/setup").WithTags("Setup").RequireRateLimiting(RateLimitPolicy);
        setup.MapGet("/status", SetupStatusAsync);
        setup.MapPost("", SetupAsync);

        var auth = api.MapGroup("/auth").WithTags("Authentication");
        auth.MapGet("/session", SessionAsync);
        auth.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/login/two-factor", TwoFactorAsync).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/logout", LogoutAsync);
        auth.MapPost("/register", RegisterAsync).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/password/forgot", ForgotPasswordAsync).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/password/reset", ResetPasswordAsync).RequireRateLimiting(RateLimitPolicy);
    }

    private static async Task<SetupStatusResponse> SetupStatusAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        new(!await SetupToken.AnyUserAsync(await unitOfWork.GetConnectionAsync(cancellationToken), cancellationToken));

    private static async Task<IResult> SetupAsync(
        SetupRequest request, IUnitOfWork unitOfWork, IInstanceSettings settings, UserManager<ArdeaUser> users,
        ArdeaSignInManager signIn, IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors()
            .Required("setupToken", request.SetupToken, "Enter the setup token.")
            .Email("email", request.Email)
            .Required("displayName", request.DisplayName, "Enter your name.")
            .MaxLength("displayName", request.DisplayName, 200)
            .Required("password", request.Password, "Enter a password.");
        if (errors.Any)
        {
            return IamErrors.Validation(errors.Errors);
        }

        // Two requests at once must not both create a first user.
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('ardea-setup'))", connection))
        {
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (await SetupToken.AnyUserAsync(connection, cancellationToken))
        {
            return IamErrors.SetupCompleted();
        }

        var expected = (await settings.GetAsync(SetupToken.Setting, cancellationToken)).Value;
        if (!SetupToken.Matches(expected, request.SetupToken!.Trim()))
        {
            return IamErrors.InvalidSetupToken();
        }

        var user = NewUser(request.Email!, request.DisplayName!, time);
        user.IsInstanceAdmin = true;
        user.EmailConfirmed = true;
        var created = await users.CreateAsync(user, request.Password!);
        if (!created.Succeeded)
        {
            return IamErrors.WeakPassword(created);
        }

        if ((await settings.GetAsync(SetupToken.Setting, cancellationToken)).Source == SettingSource.Database)
        {
            await settings.SetAsync(SetupToken.Setting, null, user.Id, cancellationToken);
        }

        await signIn.SignInAsync(user, isPersistent: true);
        await audit.RecordAsync(new AuditEntry("instance.setup_completed", "user", user.Id.ToString()) { ActorUserId = user.Id, InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>The signed-in user and the readable CSRF token; 401 when signed out, still with a token for the sign-in form.</summary>
    private static async Task<IResult> SessionAsync(
        HttpContext http, IAntiforgery antiforgery, UserManager<ArdeaUser> users, CancellationToken cancellationToken)
    {
        IssueCsrfToken(http, antiforgery);
        if (http.User.Identity?.IsAuthenticated != true || await users.GetUserAsync(http.User) is not { } user)
        {
            return Results.Unauthorized();
        }

        _ = cancellationToken;
        return Results.Ok(new SessionResponse(
            new SessionUser(user.Id, user.Email!, user.DisplayName, user.IsInstanceAdmin, user.TwoFactorEnabled),
            []));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, IUnitOfWork unitOfWork, UserManager<ArdeaUser> users, ArdeaSignInManager signIn,
        IAuditLog audit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return IamErrors.InvalidCredentials();
        }

        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            // Same work and the same answer as for a wrong password: nothing tells whether the account exists.
            users.PasswordHasher.VerifyHashedPassword(new ArdeaUser { DisplayName = "-" }, DummyHash.Value, request.Password);
            return IamErrors.InvalidCredentials();
        }

        var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            await audit.RecordAsync(new AuditEntry("auth.signed_in", "user", user.Id.ToString()) { ActorUserId = user.Id, InstanceEvent = true }, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Results.Ok(LoginResponse.SignedIn);
        }

        if (result.RequiresTwoFactor)
        {
            await unitOfWork.CommitAsync(cancellationToken);
            return Results.Ok(LoginResponse.TwoFactorRequired);
        }

        await audit.RecordAsync(
            new AuditEntry("auth.sign_in_failed", "user", user.Id.ToString(), new { lockedOut = result.IsLockedOut }) { ActorUserId = user.Id, InstanceEvent = true },
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return IamErrors.InvalidCredentials();
    }

    private static async Task<IResult> TwoFactorAsync(
        TwoFactorRequest request, IUnitOfWork unitOfWork, ArdeaSignInManager signIn, IAuditLog audit, CancellationToken cancellationToken)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return IamErrors.TwoFactorExpired();
        }

        var code = (request.Code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        var result = request.IsRecoveryCode
            ? await signIn.TwoFactorRecoveryCodeSignInAsync(request.Code?.Trim() ?? string.Empty)
            : await signIn.TwoFactorAuthenticatorSignInAsync(code, isPersistent: true, rememberClient: false);

        if (result.Succeeded)
        {
            await audit.RecordAsync(
                new AuditEntry("auth.signed_in", "user", user.Id.ToString(), new { secondFactor = request.IsRecoveryCode ? "recovery_code" : "totp" }) { ActorUserId = user.Id, InstanceEvent = true },
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Results.NoContent();
        }

        await audit.RecordAsync(
            new AuditEntry("auth.sign_in_failed", "user", user.Id.ToString(), new { step = "second_factor", lockedOut = result.IsLockedOut }) { ActorUserId = user.Id, InstanceEvent = true },
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return result.IsLockedOut ? IamErrors.InvalidCredentials() : IamErrors.InvalidCode();
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext http, IUnitOfWork unitOfWork, IamDbContext db, ArdeaSignInManager signIn, IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        if (CurrentSessionId(http.User) is { } sessionId)
        {
            await db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, time.GetUtcNow()), cancellationToken);
            await audit.RecordAsync(new AuditEntry("auth.signed_out", "session", sessionId.ToString()) { InstanceEvent = true }, cancellationToken);
        }

        await signIn.SignOutAsync();
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request, IUnitOfWork unitOfWork, IConfiguration configuration, UserManager<ArdeaUser> users,
        ArdeaSignInManager signIn, IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        var mode = (configuration["REGISTRATION"] ?? "invite").ToLowerInvariant();
        if (mode == "closed")
        {
            return IamErrors.RegistrationClosed();
        }

        var errors = new FieldErrors()
            .Email("email", request.Email)
            .Required("displayName", request.DisplayName, "Enter your name.")
            .MaxLength("displayName", request.DisplayName, 200)
            .Required("password", request.Password, "Enter a password.");
        if (errors.Any)
        {
            return IamErrors.Validation(errors.Errors);
        }

        if (mode == "invite")
        {
            // Invitations arrive with organisations (task 2.1); until then no token is valid.
            return IamErrors.InvalidInvitation();
        }

        if (await users.FindByEmailAsync(request.Email!.Trim()) is not null)
        {
            return IamErrors.EmailTaken();
        }

        var user = NewUser(request.Email!, request.DisplayName!, time);
        user.EmailConfirmed = true; // confirmation by e-mail comes with task 0.9
        var created = await users.CreateAsync(user, request.Password!);
        if (!created.Succeeded)
        {
            return created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName") ? IamErrors.EmailTaken() : IamErrors.WeakPassword(created);
        }

        await signIn.SignInAsync(user, isPersistent: true);
        await audit.RecordAsync(new AuditEntry("auth.registered", "user", user.Id.ToString()) { ActorUserId = user.Id, InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>Always 204, whether or not the account exists (master plan 9.3).</summary>
    private static Task<IResult> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        // Sending the reset link needs e-mail (task 0.9). Without SMTP the operator resets the password
        // with `ardea admin reset-password` (master plan 12.4).
        _ = request;
        return Task.FromResult(Results.NoContent());
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request, IUnitOfWork unitOfWork, UserManager<ArdeaUser> users, IamDbContext db, IAuditLog audit,
        TimeProvider time, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrEmpty(request.NewPassword))
        {
            return IamErrors.InvalidToken();
        }

        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return IamErrors.InvalidToken();
        }

        var result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return result.Errors.Any(e => e.Code == "InvalidToken") ? IamErrors.InvalidToken() : IamErrors.WeakPassword(result, "newPassword");
        }

        await RevokeSessionsAsync(db, user.Id, except: null, time, cancellationToken);
        await audit.RecordAsync(new AuditEntry("auth.password_reset", "user", user.Id.ToString()) { ActorUserId = user.Id, InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    internal static ArdeaUser NewUser(string email, string displayName, TimeProvider time) => new()
    {
        Id = Guid.CreateVersion7(),
        UserName = email.Trim(),
        Email = email.Trim(),
        DisplayName = displayName.Trim(),
        CreatedAt = time.GetUtcNow(),
    };

    internal static Guid? CurrentSessionId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(SessionClaims.SessionId), out var id) ? id : null;

    internal static Task<int> RevokeSessionsAsync(IamDbContext db, Guid userId, Guid? except, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return db.Sessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && (except == null || s.Id != except))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
    }

    internal static void IssueCsrfToken(HttpContext http, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(http);
        http.Response.Cookies.Append(SessionClaims.XsrfCookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
    }
}
