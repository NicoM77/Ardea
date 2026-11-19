using System.Security.Claims;
using Ardea.Iam.Domain;
using Ardea.Iam.Persistence;
using Ardea.Iam.Security;
using Ardea.Platform.Audit;
using Ardea.Platform.Email;
using Ardea.Platform.Http;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Settings;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ardea.Iam.Features;

/// <summary>Setup, sign-in, registration and password reset (master plan 9.1, 9.3, 10.2).</summary>
internal static class AuthEndpoints
{
    public const string RateLimitPolicy = "ardea-auth";

    /// <summary>For read-only lookups before sign-in; six times the limit of <see cref="RateLimitPolicy"/>.</summary>
    public const string ReadRateLimitPolicy = RateLimits.Lookup;

    private static readonly ArdeaUser Nobody = new() { DisplayName = "-" };
    private static string? _dummyHash;

    /// <summary>
    /// Costs as much as checking a real password: a hash made with the configured hasher, so its
    /// iteration count matches the stored hashes.
    /// </summary>
    private static void BurnPasswordCheck(UserManager<ArdeaUser> users, string password)
    {
        var hash = _dummyHash ??= users.PasswordHasher.HashPassword(Nobody, Guid.NewGuid().ToString());
        users.PasswordHasher.VerifyHashedPassword(Nobody, hash, password);
    }

    public static void Map(IEndpointRouteBuilder api)
    {
        var setup = api.MapGroup("/setup").WithTags("Setup").RequireRateLimiting(RateLimitPolicy);
        setup.MapGet("/status", SetupStatusAsync).WithName("getSetupStatus").RequireRateLimiting(ReadRateLimitPolicy);
        setup.MapPost("", SetupAsync).WithName("completeSetup").NoContent().Problems(400, 403, 409);

        var auth = api.MapGroup("/auth").WithTags("Authentication");
        auth.MapGet("/session", SessionAsync).WithName("getSession").Produces<SessionResponse>().Problems(401);
        auth.MapPost("/login", LoginAsync).WithName("login").Produces<LoginResponse>().Problems(401).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/login/two-factor", TwoFactorAsync).WithName("loginTwoFactor").NoContent().Problems(400, 401).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/logout", LogoutAsync).WithName("logout").NoContent();
        auth.MapPost("/register", RegisterAsync).WithName("register").NoContent().Problems(400, 403).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/password/forgot", ForgotPasswordAsync).WithName("forgotPassword").NoContent().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/password/reset", ResetPasswordAsync).WithName("resetPassword").NoContent().Problems(400).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/email/confirm", ConfirmEmailAsync).WithName("confirmEmail").NoContent().Problems(400).RequireRateLimiting(RateLimitPolicy);
    }

    private static async Task<SetupStatusResponse> SetupStatusAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        new(!await SetupToken.AnyUserAsync(await unitOfWork.GetConnectionAsync(cancellationToken), cancellationToken));

    private static async Task<IResult> SetupAsync(
        SetupRequest request, IUnitOfWork unitOfWork, IInstanceSettings settings, UserManager<ArdeaUser> users,
        ArdeaSignInManager signIn, IAuditLog audit, TimeProvider time, TenantContext tenant, CancellationToken cancellationToken)
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

        // The first account is the user of this unit of work from its first statement on.
        var userId = Guid.CreateVersion7();
        if (!AccountLookup.TryActAs(tenant, userId))
        {
            return IamErrors.SignedIn();
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

        var user = NewUser(request.Email!, request.DisplayName!, time, userId);
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
        HttpContext http, IAntiforgery antiforgery, UserManager<ArdeaUser> users, IamDbContext db, CancellationToken cancellationToken)
    {
        IssueCsrfToken(http, antiforgery);
        if (http.User.Identity?.IsAuthenticated != true || await users.GetUserAsync(http.User) is not { } user)
        {
            return Results.Unauthorized();
        }

        var organizations = await OrganizationDirectory.ForUserAsync(db, user.Id, cancellationToken);
        return Results.Ok(new SessionResponse(
            new SessionUser(user.Id, user.Email!, user.DisplayName, user.IsInstanceAdmin, user.TwoFactorEnabled),
            [.. organizations.Select(o => new SessionOrganization(o.Id, o.Slug, o.Name, o.Role, o.MfaRequired))]));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, IUnitOfWork unitOfWork, UserManager<ArdeaUser> users, ArdeaSignInManager signIn,
        IAuditLog audit, TenantContext tenant, NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return IamErrors.InvalidCredentials();
        }

        var userId = await AccountLookup.UserIdByEmailAsync(dataSource, users.NormalizeEmail(request.Email.Trim()), cancellationToken);
        if (userId is { } id && !AccountLookup.TryActAs(tenant, id))
        {
            return IamErrors.SignedIn();
        }

        var user = userId is null ? null : await users.FindByIdAsync(userId.Value.ToString());
        if (user is null)
        {
            // Same work and the same answer as for a wrong password: nothing tells whether the account exists.
            BurnPasswordCheck(users, request.Password);
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

        if (result.IsLockedOut)
        {
            // Identity skips the password check for a locked account; doing it anyway keeps the timing equal.
            BurnPasswordCheck(users, request.Password);
        }

        await audit.RecordAsync(
            new AuditEntry("auth.sign_in_failed", "user", user.Id.ToString(), new { lockedOut = result.IsLockedOut }) { ActorUserId = user.Id, InstanceEvent = true },
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return IamErrors.InvalidCredentials();
    }

    private static async Task<IResult> TwoFactorAsync(
        TwoFactorRequest request, HttpContext http, IUnitOfWork unitOfWork, ArdeaSignInManager signIn, IAuditLog audit, TenantContext tenant,
        CancellationToken cancellationToken)
    {
        // The account of the first step, from its short-lived cookie, is the user of this unit of work.
        var pending = await http.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        if (!Guid.TryParse(pending.Principal?.FindFirstValue(ClaimTypes.Name), out var pendingId))
        {
            return IamErrors.TwoFactorExpired();
        }

        if (!AccountLookup.TryActAs(tenant, pendingId))
        {
            return IamErrors.SignedIn();
        }

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
        RegisterRequest request, IUnitOfWork unitOfWork, IInstanceSettings settings, UserManager<ArdeaUser> users,
        ArdeaSignInManager signIn, IEmailSender email, IJobQueue jobs, IAuditLog audit, TimeProvider time, NpgsqlDataSource dataSource,
        TenantContext tenant, CancellationToken cancellationToken)
    {
        // Who this unit of work acts for is settled before it begins: the existing account of the
        // address, or the one about to be created.
        var existingId = string.IsNullOrWhiteSpace(request.Email) ? null
            : await AccountLookup.UserIdByEmailAsync(dataSource, users.NormalizeEmail(request.Email.Trim()), cancellationToken);
        var userId = existingId ?? Guid.CreateVersion7();
        if (!AccountLookup.TryActAs(tenant, userId))
        {
            return IamErrors.SignedIn();
        }

        var mode = ((await settings.GetAsync(InstanceSettingDefinitions.Registration, cancellationToken)).Value ?? "invite").ToLowerInvariant();
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

        // An invitation for this very address opens registration in the mode "invite" and confirms
        // the address; the web app accepts the invitation right after (POST /invitations/{token}/accept).
        var invited = !string.IsNullOrWhiteSpace(request.InvitationToken)
            && await OrganizationDirectory.OrganizationOfTokenAsync(dataSource, request.InvitationToken, users.NormalizeEmail(request.Email!.Trim()), cancellationToken) is not null;
        if (mode == "invite" && !invited)
        {
            return IamErrors.InvalidInvitation();
        }

        if (existingId is not null && await users.FindByIdAsync(userId.ToString()) is { } existing)
        {
            // The same answer as a new registration, without signing in: the web app then shows the
            // sign-in page. Nothing tells a stranger that the address has an account (master plan 9.3);
            // the owner of the address learns it by e-mail.
            BurnPasswordCheck(users, request.Password!);
            if (await email.IsConfiguredAsync(cancellationToken))
            {
                await jobs.EnqueueAsync(new SendAlreadyRegisteredNotice(existing.Id), new EnqueueOptions { CoalesceKey = $"iam.already-registered:{existing.Id}" }, cancellationToken);
                await unitOfWork.CommitAsync(cancellationToken);
            }

            return Results.NoContent();
        }

        var user = NewUser(request.Email!, request.DisplayName!, time, userId);
        // Without SMTP addresses count as confirmed (master plan 12.4); with it, a mail confirms them.
        var withMail = await email.IsConfiguredAsync(cancellationToken) && !invited;
        user.EmailConfirmed = !withMail;
        IdentityResult created;
        try
        {
            created = await users.CreateAsync(user, request.Password!);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Registered at the same moment by another request: the same answer as for any existing account.
            return Results.NoContent();
        }
        if (!created.Succeeded)
        {
            return created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName") ? Results.NoContent() : IamErrors.WeakPassword(created);
        }

        if (withMail)
        {
            await jobs.EnqueueAsync(new SendEmailConfirmation(user.Id), cancellationToken: cancellationToken);
        }

        await signIn.SignInAsync(user, isPersistent: true);
        await audit.RecordAsync(new AuditEntry("auth.registered", "user", user.Id.ToString()) { ActorUserId = user.Id, InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// Always 204, whether or not the account exists (master plan 9.3). With SMTP a job sends the link;
    /// without it the operator resets the password with <c>ardea admin reset-password</c> (master plan 12.4).
    /// </summary>
    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request, IUnitOfWork unitOfWork, UserManager<ArdeaUser> users, IEmailSender email, IJobQueue jobs,
        IAuditLog audit, TenantContext tenant, NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        var userId = string.IsNullOrWhiteSpace(request.Email) ? null
            : await AccountLookup.UserIdByEmailAsync(dataSource, users.NormalizeEmail(request.Email.Trim()), cancellationToken);
        if (userId is { } id && AccountLookup.TryActAs(tenant, id)
            && await email.IsConfiguredAsync(cancellationToken)
            && await users.FindByIdAsync(id.ToString()) is { } user)
        {
            // At most one waiting mail per account, however often the form is sent.
            await jobs.EnqueueAsync(new SendPasswordResetEmail(user.Id), new EnqueueOptions { CoalesceKey = $"iam.password-reset:{user.Id}" }, cancellationToken);
            await audit.RecordAsync(new AuditEntry("auth.password_reset_requested", "user", user.Id.ToString()) { ActorUserId = user.Id, InstanceEvent = true }, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ConfirmEmailAsync(
        ConfirmEmailRequest request, IUnitOfWork unitOfWork, UserManager<ArdeaUser> users, IAuditLog audit, TenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (request.UserId is not { } userId || string.IsNullOrWhiteSpace(request.Token)
            || !AccountLookup.TryActAs(tenant, userId)
            || await users.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return IamErrors.InvalidToken();
        }

        var result = await users.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            return IamErrors.InvalidToken();
        }

        await audit.RecordAsync(new AuditEntry("account.email_confirmed", "user", user.Id.ToString()) { ActorUserId = user.Id, InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request, IUnitOfWork unitOfWork, UserManager<ArdeaUser> users, IamDbContext db, IAuditLog audit,
        TimeProvider time, TenantContext tenant, NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrEmpty(request.NewPassword))
        {
            return IamErrors.InvalidToken();
        }

        var userId = await AccountLookup.UserIdByEmailAsync(dataSource, users.NormalizeEmail(request.Email.Trim()), cancellationToken);
        var user = userId is { } id && AccountLookup.TryActAs(tenant, id) ? await users.FindByIdAsync(id.ToString()) : null;
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

    internal static ArdeaUser NewUser(string email, string displayName, TimeProvider time, Guid id) => new()
    {
        Id = id,
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
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
    }
}
