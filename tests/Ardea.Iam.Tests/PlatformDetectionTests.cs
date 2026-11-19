using Ardea.Assets.Repositories.Detection;
using Ardea.Catalog.Contracts;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Iam.Tests;

/// <summary>Detection rules for platform files with the shipped catalogue (task 2.12; master plan 16.3, 16.9).</summary>
public sealed class PlatformDetectionTests(PostgresServer server)
{
    [Fact]
    public async Task Ardeas_own_dockerfile_suggests_node_the_sdk_and_aspnet_core_with_lines_and_evidence()
    {
        var dockerfile = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "deploy", "Dockerfile"), TestContext.Current.CancellationToken);

        var suggestions = await DetectAsync(new RepositoryFile("deploy/Dockerfile", dockerfile));

        Assert.Equal(["aspnetcore 10.0", "dotnet 10.0", "nodejs 24"], suggestions.Select(s => $"{s.ProductKey} {s.VersionLine}"));
        var aspnet = suggestions.Single(s => s.ProductKey == "aspnetcore").Evidence.Single();
        Assert.Equal("deploy/Dockerfile", aspnet.File);
        Assert.StartsWith("FROM mcr.microsoft.com/dotnet/aspnet:10.0", aspnet.Value, StringComparison.Ordinal);
        Assert.Equal(dockerfile.ReplaceLineEndings("\n").Split('\n').ToList().FindIndex(l => l.StartsWith("FROM mcr.microsoft.com/dotnet/aspnet", StringComparison.Ordinal)) + 1, aspnet.Line);
    }

    [Fact]
    public async Task Stages_scratch_unresolved_variables_and_unknown_images_suggest_nothing_but_arg_defaults_resolve()
    {
        var suggestions = await DetectAsync(new RepositoryFile("Dockerfile", """
            ARG PG=16.4
            FROM postgres:${PG}-alpine AS db
            FROM db
            FROM scratch
            FROM ${BASE_IMAGE}
            FROM registry.example.com:5000/acme/internal-tool:1.2@sha256:abcdef
            FROM nginx@sha256:0123456789abcdef
            """));

        Assert.Equal(["nginx ", "postgresql 16.4"], suggestions.Select(s => $"{s.ProductKey} {s.VersionLine}"));
    }

    [Fact]
    public async Task Compose_project_files_and_package_managers_give_their_platform_lines()
    {
        var suggestions = await DetectAsync(
            new RepositoryFile("deploy/compose.yaml", """
                services:
                  db:
                    image: "postgres:17"   # main database
                  cache:
                    image: redis:7.4-alpine
                  app:
                    image: ${APP_IMAGE}
                """),
            new RepositoryFile("global.json", """{ "sdk": { "version": "10.0.404", "rollForward": "latestFeature" } }"""),
            new RepositoryFile("src/Web/Web.csproj", """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                </Project>
                """),
            new RepositoryFile("legacy/Old.csproj", """<Project><PropertyGroup><TargetFrameworks>net48;netstandard2.0</TargetFrameworks></PropertyGroup></Project>"""),
            new RepositoryFile("web/package.json", """{ "name": "web", "engines": { "node": ">=22.12 <25" } }"""),
            new RepositoryFile("web/.nvmrc", "v24.11.1\n"),
            new RepositoryFile("shop/composer.json", """{ "require": { "php": "^8.3", "laravel/framework": "^11.0" } }"""),
            new RepositoryFile("tools/go.mod", "module example.com/tool\n\ngo 1.23.4\n\nrequire golang.org/x/net v0.30.0\n"));

        Assert.Equal(
            ["aspnetcore 10.0", "dotnet 10.0", "dotnet-framework 4.8", "go 1.23", "nodejs 22", "nodejs 24", "php 8.3", "postgresql 17", "redis 7.4"],
            suggestions.Select(s => $"{s.ProductKey} {s.VersionLine}"));
        // global.json and the project file both say .NET 10.0: one suggestion with both as evidence.
        Assert.Equal(["global.json", "src/Web/Web.csproj"], suggestions.Single(s => s.ProductKey == "dotnet").Evidence.Select(e => e.File).Order());
        Assert.Equal(3, suggestions.Single(s => s.ProductKey == "postgresql").Evidence.Single().Line);
    }

    [Fact]
    public async Task Hostile_files_are_read_as_data_only()
    {
        var suggestions = await DetectAsync(
            new RepositoryFile("evil.csproj", """
                <?xml version="1.0"?>
                <!DOCTYPE p [ <!ENTITY x SYSTEM "file:///etc/passwd"> ]>
                <Project><PropertyGroup><TargetFramework>&x;</TargetFramework></PropertyGroup></Project>
                """),
            new RepositoryFile("package.json", "{ not json"),
            new RepositoryFile("composer.json", """{ "require": { "php": 8 } }"""),
            new RepositoryFile(".nvmrc", "lts/iron"));

        Assert.Empty(suggestions);
    }

    [Theory]
    [InlineData("deploy/Dockerfile", true)]
    [InlineData("Dockerfile.prod", true)]
    [InlineData("api.dockerfile", true)]
    [InlineData("docker-compose.override.yml", true)]
    [InlineData("compose.yaml", true)]
    [InlineData("src/App/App.csproj", true)]
    [InlineData("web/package.json", true)]
    [InlineData("web/package-lock.json", false)]
    [InlineData("README.md", false)]
    [InlineData("go.sum", false)]
    public void Only_known_files_are_fetched(string path, bool candidate) =>
        Assert.Equal(candidate, PlatformDetection.IsCandidate(path));

    [Theory]
    [InlineData("10.0-noble", "10.0")]
    [InlineData("18-alpine", "18")]
    [InlineData("8.4.3-fpm", "8.4")]
    [InlineData("v20.11.1", "20.11")]
    [InlineData("latest", null)]
    [InlineData(null, null)]
    public void Tags_become_version_lines(string? tag, string? line) =>
        Assert.Equal(line, PlatformDetection.VersionLine(tag));

    private async Task<IReadOnlyList<PlatformSuggestion>> DetectAsync(params RepositoryFile[] files)
    {
        await using var host = await ApiTestHost.StartAsync(server);
        await using var scope = host.Services.CreateAsyncScope();
        return await PlatformDetection.DetectAsync(files, scope.ServiceProvider.GetRequiredService<IProductCatalog>(), TestContext.Current.CancellationToken);
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
