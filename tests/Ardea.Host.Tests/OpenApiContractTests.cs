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

    /// <summary>
    /// Phase 5a acceptance (master plan 14, 17.5; ADR 046): no endpoint knows assignment, deadline or «in review». The
    /// committed document equals the code (the test above), so its schemas, parameters and enumerations speak for every
    /// endpoint. «assigner» (who scored a CVSS vector) is not an assignment.
    /// </summary>
    [Fact]
    public async Task No_endpoint_knows_assignment_deadline_or_review_state()
    {
        var path = Path.Combine(RepositoryRoot(), "openapi", "ardea.v1.json");
        using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        var retired = new System.Text.RegularExpressions.Regex(
            @"(un)?assign(?!er)|^due$|dueat|due_at|deadline|overdue|in_?review|remaining",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var found = new List<string>();

        void Walk(System.Text.Json.JsonElement element, string at)
        {
            switch (element.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var here = $"{at}/{property.Name}";
                        if (property.Name == "properties" && property.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            found.AddRange(property.Value.EnumerateObject().Where(p => retired.IsMatch(p.Name)).Select(p => $"{here}/{p.Name}"));
                        }
                        else if (property.Name == "enum" && property.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            found.AddRange(property.Value.EnumerateArray()
                                .Where(v => v.ValueKind == System.Text.Json.JsonValueKind.String && retired.IsMatch(v.GetString()!)).Select(v => $"{here}: {v.GetString()}"));
                        }
                        else if (property.Name == "name" && at.Contains("/parameters", StringComparison.Ordinal) && retired.IsMatch(property.Value.GetString() ?? string.Empty))
                        {
                            found.Add($"{here}: {property.Value.GetString()}");
                        }

                        Walk(property.Value, here);
                    }

                    break;
                case System.Text.Json.JsonValueKind.Array:
                    var i = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item, $"{at}[{i++}]");
                    }

                    break;
            }
        }

        Walk(document.RootElement, string.Empty);
        found.AddRange(document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).Where(p => retired.IsMatch(p)));
        found.AddRange(document.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(s => s.Name).Where(s => retired.IsMatch(s)));
        Assert.Empty(found);
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
