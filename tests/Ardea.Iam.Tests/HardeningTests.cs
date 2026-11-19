using System.Net;
using System.Text.Json;
using Ardea.Platform.Http;
using Ardea.TestSupport;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Iam.Tests;

/// <summary>The security pass of task 6.1 along the threat table of master plan 9.7, where the API pipeline is the proof.</summary>
public sealed class HardeningTests(PostgresServer server)
{
    private const string Email = "first@example.org";

    [Fact]
    public async Task Error_answers_rejections_and_unknown_paths_carry_the_security_headers_and_no_internals()
    {
        const string Internal = "connection to 10.0.0.5:5432 failed for ardea_app";
        await using var host = await ApiTestHost.StartAsync(
            server,
            new() { ["RATE_LIMIT:AUTH_PER_MINUTE"] = "1" },
            services => services.AddScoped<Platform.Persistence.IUnitOfWork>(_ => throw new InvalidOperationException(Internal)));
        using var browser = host.Client();

        // The exception handler clears the response before it writes its problem; the headers must survive that.
        var failed = await browser.GetAsync("/api/v1/setup/status");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.DoesNotContain(Internal, await failed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        AssertSecurityHeaders(failed);
        Assert.True(failed.Headers.CacheControl?.NoStore);

        AssertSecurityHeaders(await browser.GetAsync("/api/v1/no-such-endpoint"));

        using var tooLarge = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent(new string(' ', 2 * 1024 * 1024), System.Text.Encoding.UTF8, "application/json"),
        };
        var refused = await browser.SendRawAsync(tooLarge);
        await ExpectProblem(refused, HttpStatusCode.RequestEntityTooLarge, "request_too_large");
        AssertSecurityHeaders(refused);

        HttpResponseMessage limited = null!;
        for (var i = 0; i < 7; i++)
        {
            limited = await browser.GetAsync("/api/v1/setup/status");
        }

        await ExpectProblem(limited, HttpStatusCode.TooManyRequests, "rate_limited");
        AssertSecurityHeaders(limited);
    }

    [Fact]
    public async Task The_session_cookie_is_strict_and_a_copy_of_it_ends_with_the_sign_out()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();
        var setup = await browser.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = Email, displayName = "First Person", password = ApiTestHost.Password });
        Assert.Equal(HttpStatusCode.NoContent, setup.StatusCode);

        var cookie = setup.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-ardea=", StringComparison.Ordinal)).ToLowerInvariant();
        Assert.Contains("; secure", cookie, StringComparison.Ordinal);
        Assert.Contains("; httponly", cookie, StringComparison.Ordinal);
        Assert.Contains("; samesite=lax", cookie, StringComparison.Ordinal);
        Assert.Contains("; path=/", cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("domain=", cookie, StringComparison.Ordinal);

        // Someone who copied the cookie (a stolen session) acts as the user until the user signs out, not after.
        using var thief = host.Client();
        var stolen = browser.Cookies.GetCookies(new Uri("https://localhost")).Single(c => c.Name == "__Host-ardea");
        thief.Cookies.Add(new Uri("https://localhost"), new Cookie(stolen.Name, stolen.Value, "/"));
        Assert.Equal(HttpStatusCode.OK, (await thief.SessionAsync()).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/v1/auth/logout")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await thief.SessionAsync()).StatusCode);
    }

    [Fact]
    public async Task Calls_out_of_the_instance_and_links_without_a_session_are_rate_limited()
    {
        await using var host = await ApiTestHost.StartAsync(server, new()
        {
            ["RATE_LIMIT:AUTH_PER_MINUTE"] = "1",
            ["RATE_LIMIT:OUTBOUND_PER_MINUTE"] = "2",
        });
        using var admin = host.Client();
        await admin.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = Email, displayName = "First Person", password = ApiTestHost.Password });

        // The test e-mail without SMTP answers at once; the limit counts the attempts all the same.
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await admin.PostAsync("/api/v1/instance/smtp/test", new { to = Email })).StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses[..2]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[2]);

        // The unsubscribe links of external addresses carry their own token: no session, but a limit per client.
        using var stranger = host.Client();
        var links = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
        {
            links.Add((await stranger.PostAsync("/api/v1/notify/addresses/unsubscribe", new { token = "guessed" }, csrf: false)).StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, links[..6]);
        Assert.Equal(HttpStatusCode.TooManyRequests, links[6]);
    }

    /// <summary>
    /// The only changing endpoints without the CSRF check: links in e-mails to external addresses, which carry their
    /// own token and no session (<c>AuthenticatedByToken</c>). A new exemption must be added here on purpose.
    /// </summary>
    private static readonly string[] TokenAuthenticated =
    [
        "POST /api/v1/notify/addresses/confirm",
        "POST /api/v1/notify/addresses/preview",
        "POST /api/v1/notify/addresses/resubscribe",
        "POST /api/v1/notify/addresses/unsubscribe",
        "POST /api/v1/notify/addresses/unsubscribe/one-click",
    ];

    [Fact]
    public async Task Every_changing_endpoint_refuses_a_request_without_the_csrf_token_or_from_another_origin()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var admin = host.Client();
        var setup = await admin.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = Email, displayName = "First Person", password = ApiTestHost.Password });
        Assert.Equal(HttpStatusCode.NoContent, setup.StatusCode);
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(m => m is not ("GET" or "HEAD" or "OPTIONS"))
                .Select(m => (Method: m, Route: "/" + e.RoutePattern.RawText!.TrimStart('/'), Exempt: e.Metadata.GetMetadata<TokenAuthenticated>() is not null)))
            .Where(e => e.Route.StartsWith("/api/", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(endpoints);

        var problems = new List<string>();
        foreach (var (method, route, _) in endpoints.Where(e => !e.Exempt))
        {
            var path = System.Text.RegularExpressions.Regex.Replace(route, @"\{(\w+)(:(\w+))?\}", m => m.Groups[3].Value switch
            {
                "guid" => Guid.CreateVersion7().ToString(),
                "int" => "1",
                _ => "x",
            });
            foreach (var (what, response) in new[]
            {
                ("without the token", await admin.SendAsync(new HttpMethod(method), path, new { }, csrf: false)),
                ("from another origin", await admin.SendAsync(new HttpMethod(method), path, new { }, origin: "https://evil.example")),
            })
            {
                var body = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.Forbidden || !body.Contains("\"csrf_failed\"", StringComparison.Ordinal))
                {
                    problems.Add($"{method} {route} {what}: {(int)response.StatusCode} {body}");
                }
            }
        }

        Assert.Empty(problems);
        Assert.Equal(TokenAuthenticated, endpoints.Where(e => e.Exempt).Select(e => $"{e.Method} {e.Route}").Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await admin.SessionAsync()).StatusCode);
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        string Header(string name) => response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;
        Assert.Contains("frame-ancestors 'none'", Header("Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", Header("Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Equal("nosniff", Header("X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header("Referrer-Policy"));
        Assert.Equal("DENY", Header("X-Frame-Options"));
        Assert.NotEmpty(Header("Permissions-Policy"));
    }

    private static async Task ExpectProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {(int)status}, got {(int)response.StatusCode}: {body}");
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }
}
