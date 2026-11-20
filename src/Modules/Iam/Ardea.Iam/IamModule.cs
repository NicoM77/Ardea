using System.Globalization;
using System.Threading.RateLimiting;
using Ardea.Iam.Domain;
using Ardea.Iam.Features;
using Ardea.Iam.Persistence;
using Ardea.Iam.Security;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Iam;

public sealed class IamModule : IModule
{
    public string Name => IamDbContext.Schema;

    public IModuleDatabase? Database { get; } =
        new ModuleDatabase<IamDbContext>(IamDbContext.Schema, options => new IamDbContext(options, new TenantContext()));

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IamDbContext>(IamDbContext.Schema);
        services.AddHttpContextAccessor();
        services.AddIdentityCore<ArdeaUser>(options =>
            {
                options.Password.RequiredLength = CommonPasswordValidator.MinimumLength;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = string.Empty;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddUserStore<ProtectedUserStore>()
            .AddSignInManager<ArdeaSignInManager>()
            .AddPasswordValidator<CommonPasswordValidator>()
            .AddClaimsPrincipalFactory<ArdeaClaimsFactory>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<SingleUseAuthenticatorTokenProvider>(TokenOptions.DefaultAuthenticatorProvider);
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
        services.AddJob<SendPasswordResetEmail, AccountEmailHandlers>(new JobKindOptions { MaxAttempts = 3, Timeout = TimeSpan.FromMinutes(1) });
        services.AddJob<SendEmailConfirmation, AccountEmailHandlers>(new JobKindOptions { MaxAttempts = 3, Timeout = TimeSpan.FromMinutes(1) });
        services.AddJob<SendAlreadyRegisteredNotice, AccountEmailHandlers>(new JobKindOptions { MaxAttempts = 3, Timeout = TimeSpan.FromMinutes(1) });
        services.AddJob<SendInvitationEmail, InvitationEmailHandler>(new JobKindOptions { MaxAttempts = 3, Timeout = TimeSpan.FromMinutes(1) });
        services.AddScoped<IOrganizationMembership, MembershipCheck>();
        services.AddScoped<Contracts.IOrganizationProfiles, Features.OrganizationProfiles>();
        services.AddScoped<Contracts.IOrganizationMembers, Features.OrganizationMembers>();
        services.AddScoped<Contracts.IInstanceAdministrators, Features.InstanceAdministrators>();
        services.AddSingleton<IDemoSeedStep, Features.DemoSeed>();
        if (int.TryParse(configuration["PASSWORD_HASH_ITERATIONS"], CultureInfo.InvariantCulture, out var iterations) && iterations > 0)
        {
            services.Configure<PasswordHasherOptions>(o => o.IterationCount = iterations);
        }
    }

    public void AddApiServices(IServiceCollection services, IConfiguration configuration)
    {
        var secure = IsSecure(configuration);
        var policy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = SessionClaims.CookieName(secure);
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = policy;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = UserSession.IdleTimeout;
            options.SlidingExpiration = true;
            options.Events = new CookieAuthenticationEvents
            {
                OnValidatePrincipal = SessionValidator.ValidateAsync,
                OnRedirectToLogin = context => Status(context.Response, StatusCodes.Status401Unauthorized),
                OnRedirectToAccessDenied = context => Status(context.Response, StatusCodes.Status403Forbidden),
            };
        });
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
        {
            options.Cookie.Name = SessionClaims.TwoFactorCookieName(secure);
            options.Cookie.SecurePolicy = policy;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            options.SlidingExpiration = false;
        });
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ExternalScheme, options => options.Cookie.SecurePolicy = policy);
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorRememberMeScheme, options => options.Cookie.SecurePolicy = policy);
        services.AddAuthorization(options => options.AddPolicy(
            Platform.Http.Policies.InstanceAdmin, policy => policy.RequireAuthenticatedUser().RequireClaim(Contracts.IamPolicies.InstanceAdminClaim, "true")));
        services.AddAntiforgery(options =>
        {
            options.HeaderName = SessionClaims.XsrfHeaderName;
            options.Cookie.Name = SessionClaims.CsrfCookieName(secure);
            options.Cookie.SecurePolicy = policy;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
        });

        var perMinute = int.TryParse(configuration["RATE_LIMIT:AUTH_PER_MINUTE"], CultureInfo.InvariantCulture, out var limit) && limit > 0 ? limit : 10;
        var outboundPerMinute = int.TryParse(configuration["RATE_LIMIT:OUTBOUND_PER_MINUTE"], CultureInfo.InvariantCulture, out var outbound) && outbound > 0 ? outbound : 10;
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(http.Connection.RemoteIpAddress),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy(AuthEndpoints.ReadRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(http.Connection.RemoteIpAddress),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute * 6, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy(Platform.Http.RateLimits.Outbound, http => RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value is { } user ? "user:" + user : ClientKey(http.Connection.RemoteIpAddress),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = outboundPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await Platform.Http.ApiProblems.Problem("rate_limited", StatusCodes.Status429TooManyRequests, "Too many attempts. Wait a moment and try again.")
                    .ExecuteAsync(context.HttpContext);
            };
        });
        services.AddHostedService<SetupTokenAnnouncer>();
    }

    public IReadOnlyList<MaintenanceCommand> Commands { get; } = [ResetPasswordCommand.Definition];

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AuthEndpoints.Map(endpoints);
        AccountEndpoints.Map(endpoints);
        OrganizationEndpoints.Map(endpoints);
        SetupEndpoints.Map(endpoints);
        AuditEndpoints.Map(endpoints);
    }

    internal static bool IsSecure(IConfiguration configuration) =>
        !Uri.TryCreate(configuration["PUBLIC_URL"], UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttp;

    internal static string ClientKey(System.Net.IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new System.Net.IPAddress(bytes) + "/64";
    }

    private static Task Status(HttpResponse response, int status)
    {
        response.StatusCode = status;
        return Task.CompletedTask;
    }
}
