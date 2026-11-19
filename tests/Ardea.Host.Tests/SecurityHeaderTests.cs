using System.Net;
using Ardea.Host.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace Ardea.Host.Tests;

/// <summary>
/// The web app's own files carry the header set of master plan 9.6 like every API answer: index.html, the hashed
/// bundles and the single-page fallback for client routes (task 6.1).
/// </summary>
public sealed class SecurityHeaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ardea-web-").FullName;

    [Theory]
    [InlineData("/")]
    [InlineData("/main-ABCD1234.js")]
    [InlineData("/o/acme/actions/143")]
    public async Task Files_of_the_web_app_and_its_fallback_carry_the_security_headers(string path)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "index.html"), "<!doctype html><title>Ardea</title>", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_root, "main-ABCD1234.js"), "console.log(1);", TestContext.Current.CancellationToken);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { WebRootPath = _root, EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.Use(RequestProtection.SecurityHeaders);
        WebApp.Use(app);
        WebApp.Map(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("script-src 'self'", string.Join(",", response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", string.Join(",", response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
