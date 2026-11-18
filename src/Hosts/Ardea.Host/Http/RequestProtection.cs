using System.Net;
using System.Security.Claims;
using Ardea.Platform.Http;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;

namespace Ardea.Host.Http;

/// <summary>Cross-cutting protection of every request (master plan 9.3, 9.6).</summary>
internal static class RequestProtection
{
    /// <summary>
    /// Behind the bundled proxy or a tunnel, client address and scheme arrive in forwarded headers.
    /// They are trusted only from loopback, plus the addresses or CIDR ranges in
    /// <c>ARDEA_TRUSTED_PROXIES</c> (comma-separated). Trusting more would let anything that reaches
    /// the API from there choose its client address, and so its rate-limit bucket.
    /// </summary>
    public static void ConfigureForwardedHeaders(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Walk back through every trusted proxy, so a chain of two still yields the real client.
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

    /// <summary>Header set of master plan 9.6. HSTS is the proxy's job.</summary>
    public static Task SecurityHeaders(HttpContext context, RequestDelegate next)
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
            // API answers hold personal data and secrets (MFA keys, recovery codes): never cache them.
            headers.CacheControl = "no-store";
        }

        return next(context);
    }

    /// <summary>The signed-in user becomes part of the tenant context before any data access.</summary>
    public static Task TenantUser(HttpContext context, RequestDelegate next)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            context.RequestServices.GetRequiredService<TenantContext>().SetUser(userId);
        }

        return next(context);
    }

    /// <summary>
    /// CSRF protection for every changing API request (master plan 9.3): browser fetch metadata and
    /// origin must point to this instance, and the antiforgery token must be in the header.
    /// </summary>
    public static RequestDelegate Csrf(RequestDelegate next, string publicOrigin) => async context =>
    {
        if (!context.Request.Path.StartsWithSegments("/api") || HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method))
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
