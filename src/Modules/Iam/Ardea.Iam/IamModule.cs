using System.Globalization;
using System.Threading.RateLimiting;
using Ardea.Iam.Domain;
using Ardea.Iam.Features;
using Ardea.Iam.Persistence;
using Ardea.Iam.Security;
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
                // Master plan 9.3: length 12, no composition rules; the common-password list does the rest.
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
            .AddDefaultTokenProviders();
        if (int.TryParse(configuration["PASSWORD_HASH_ITERATIONS"], CultureInfo.InvariantCulture, out var iterations) && iterations > 0)
        {
            services.Configure<PasswordHasherOptions>(o => o.IterationCount = iterations);
        }
    }

    public void AddApiServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = SessionClaims.CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = UserSession.IdleTimeout;
            options.SlidingExpiration = true;
            options.Events = new CookieAuthenticationEvents
            {
                OnValidatePrincipal = SessionValidator.ValidateAsync,
                // An API answers with a status code, never with a redirect to a sign-in page.
                OnRedirectToLogin = context => Status(context.Response, StatusCodes.Status401Unauthorized),
                OnRedirectToAccessDenied = context => Status(context.Response, StatusCodes.Status403Forbidden),
            };
        });
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
        {
            options.Cookie.Name = "__Host-ardea-2fa";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            // Five minutes from the password, however many codes are tried in between.
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            options.SlidingExpiration = false;
        });
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ExternalScheme, options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorRememberMeScheme, options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);
        services.AddAuthorization();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = SessionClaims.XsrfHeaderName;
            options.Cookie.Name = SessionClaims.CsrfCookieName;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
        });

        var perMinute = int.TryParse(configuration["RATE_LIMIT:AUTH_PER_MINUTE"], CultureInfo.InvariantCulture, out var limit) && limit > 0 ? limit : 10;
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Per client network: an IPv6 client controls a whole /64, so the limit applies to it as one.
            // Per account, the lockout after five failures limits guessing (master plan 9.3).
            options.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(http.Connection.RemoteIpAddress),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
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

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AuthEndpoints.Map(endpoints);
        AccountEndpoints.Map(endpoints);
    }

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
