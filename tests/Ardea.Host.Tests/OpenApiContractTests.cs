using System.Security.Cryptography;
using Ardea.Host.Commands;
using Ardea.Host.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ardea.Host.Tests;

/// <summary>
/// Master plan 10.1: the OpenAPI document is committed, and the build fails when code and file drift
/// apart. Run with <c>ARDEA_UPDATE_OPENAPI=1</c> to rewrite <c>openapi/ardea.v1.json</c> after an
/// intended API change; the web client is then regenerated from it (<c>npm run generate:api</c>).
/// </summary>
public sealed class OpenApiContractTests
{
    [Fact]
    public async Task Committed_openapi_document_matches_the_code()
    {
        var app = ApiRole.Build([], out var check, builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_HOST"] = "localhost",
                ["DB_APP_PASSWORD"] = "unused",
                ["MASTER_KEY"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["PUBLIC_URL"] = "https://localhost",
                ["SETUP_TOKEN"] = "unused",
            });
        }) ?? throw new InvalidOperationException(string.Join(" ", check.Errors));
        await using var _ = app;
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var actual = ApiDocument.Normalize(await client.GetStringAsync(new Uri("/api/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken));
        var path = Path.Combine(RepositoryRoot(), "openapi", "ardea.v1.json");

        if (Environment.GetEnvironmentVariable("ARDEA_UPDATE_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual, TestContext.Current.CancellationToken);
        }

        var committed = File.Exists(path) ? await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken) : string.Empty;
        Assert.True(
            committed == actual,
            "openapi/ardea.v1.json differs from the API. If the change is intended, run " +
            "`ARDEA_UPDATE_OPENAPI=1 dotnet test --project tests/Ardea.Host.Tests`, then `npm run generate:api` in web/, and commit both.");
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Descriptions_have_the_same_line_breaks_on_every_machine()
    {
        var normalized = ApiDocument.Normalize("""{"a":{"description":"one\r\ntwo"},"b":["x\r\ny"]}""");

        Assert.DoesNotContain("\\r", normalized, StringComparison.Ordinal);
        Assert.Contains("one\\ntwo", normalized, StringComparison.Ordinal);
        Assert.Contains("x\\ny", normalized, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ardea.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
