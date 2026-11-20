using System.Net;
using System.Security.Claims;
using Ardea.Platform.Http;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;

namespace Ardea.Host.Http;

internal static class RequestProtection
{
    public static void ConfigureForwardedHeaders(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = null;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            var configured = configuration["TRUSTED_PROXIES"];
            string[] entries =
            [
                "127.0.0.0/8",
                "::1/128",
                .. (configured ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ];
            foreach (var entry in entries)
            {
                if (System.Net.IPNetwork.TryParse(entry, out var network))
                {
                    options.KnownIPNetworks.Add(network);
                }
                else if (IPAddress.TryParse(entry, out var address))
                {
                    options.KnownProxies.Add(address);
                }
                else
                {
                    throw new InvalidOperationException($"ARDEA_TRUSTED_PROXIES contains '{entry}', which is neither an address nor a CIDR range.");
                }
            }
        });
    }

    public const long MaxRequestBodyBytes = 1024 * 1024;

    public static Task SecurityHeaders(HttpContext context, RequestDelegate next)
    {
        context.Response.OnStarting(
            static state =>
            {
                ApplySecurityHeaders((HttpContext)state);
                return Task.CompletedTask;
            },
            context);
        return next(context);
    }

    private static void ApplySecurityHeaders(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        headers.XFrameOptions = "DENY";
        headers.ContentSecurityPolicy =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
            "connect-src 'self'; manifest-src 'self'; worker-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers.CacheControl = "no-store";
        }
    }

    public static Task LimitBody(HttpContext context, RequestDelegate next)
    {
        if (context.Request.ContentLength > MaxRequestBodyBytes)
        {
            return ApiProblems.Problem("request_too_large", StatusCodes.Status413PayloadTooLarge, "The request is too large.")
                .ExecuteAsync(context);
        }

        return next(context);
    }

    public static RequestDelegate RequireHttps(RequestDelegate next, bool required) => context =>
    {
        if (required && !context.Request.IsHttps && context.Request.Path.StartsWithSegments("/api"))
        {
            return ApiProblems.Problem(
                "https_required", StatusCodes.Status403Forbidden,
                "This instance answers only over HTTPS. Open it through its public address, or check that the proxy sends X-Forwarded-Proto and is listed in ARDEA_TRUSTED_PROXIES.")
                .ExecuteAsync(context);
        }

        return next(context);
    };

    public static Task TenantUser(HttpContext context, RequestDelegate next)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            context.RequestServices.GetRequiredService<TenantContext>().SetUser(userId);
        }

        return next(context);
    }

    public static RequestDelegate Csrf(RequestDelegate next, string publicOrigin) => async context =>
    {
        if (!context.Request.Path.StartsWithSegments("/api") || HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method)
            || context.GetEndpoint()?.Metadata.GetMetadata<TokenAuthenticated>() is not null)
        {
            await next(context);
            return;
        }

        var site = context.Request.Headers["Sec-Fetch-Site"].ToString();
        var origin = context.Request.Headers.Origin.ToString();
        var crossSite = (site.Length > 0 && site is not ("same-origin" or "none"))
            || (origin.Length > 0 && !string.Equals(origin, publicOrigin, StringComparison.OrdinalIgnoreCase));
        if (crossSite)
        {
            await Reject(context, "The request does not come from this instance.");
            return;
        }

        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            await Reject(context, "The security token is missing or expired. Reload the page and try again.");
            return;
        }

        await next(context);
    };

    private static Task Reject(HttpContext context, string title) =>
        ApiProblems.Problem("csrf_failed", StatusCodes.Status403Forbidden, title).ExecuteAsync(context);

    public static string Origin(string publicUrl)
    {
        var uri = new Uri(publicUrl);
        return uri.GetLeftPart(UriPartial.Authority);
    }
}
