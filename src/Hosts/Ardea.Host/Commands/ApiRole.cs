using Ardea.Platform.Configuration;
using Ardea.Platform.Hosting;
using Ardea.Platform.Persistence;
using Npgsql;

namespace Ardea.Host.Commands;

/// <summary>The <c>api</c> role: REST API, sign-in, web app and live events (master plan 3.1).</summary>
internal static partial class ApiRole
{
    public static async Task<int> RunAsync(string[] args)
    {
        var app = Build(args, out var check);
        if (app is null)
        {
            return HostConfiguration.Fail(check);
        }

        var behind = await SchemaGate.FindBehindAsync(
            app.Services.GetRequiredService<NpgsqlDataSource>(), app.Services.GetRequiredService<DatabaseOptions>(), CancellationToken.None);
        if (behind.Count > 0)
        {
            await Console.Error.WriteLineAsync($"The database schema is older than this version ({string.Join(", ", behind)}). Run `ardea migrate` first.");
            return 75;
        }

        await app.RunAsync();
        return 0;
    }

    /// <summary>Builds the API application; <c>null</c> when the configuration is invalid.</summary>
    public static WebApplication? Build(string[] args, out ConfigurationCheck check, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ApplicationName = typeof(ApiRole).Assembly.GetName().Name });
        builder.Configuration.AddArdeaEnvironment();
        configure?.Invoke(builder);

        check = HostConfiguration.CheckApplication(builder.Configuration);
        if (!check.IsValid)
        {
            return null;
        }

        HostConfiguration.AddJsonLogging(builder.Logging);
        var port = int.TryParse(builder.Configuration["HTTP_PORT"], out var p) ? p : 8080;
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

        builder.Services.AddArdeaPlatform(builder.Configuration, "api");
        builder.Services.AddProblemDetails();
        foreach (var module in Modules.All)
        {
            module.AddServices(builder.Services, builder.Configuration);
        }

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).ExcludeFromDescription();
        app.MapGet("/health/ready", ReadyAsync).ExcludeFromDescription();

        var api = app.MapGroup("/api/v1");
        foreach (var module in Modules.All)
        {
            module.MapEndpoints(api);
        }

        return app;
    }

    private static async Task<IResult> ReadyAsync(NpgsqlDataSource dataSource, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return Results.Ok(new { status = "ready" });
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            LogNotReady(loggers.CreateLogger("Ardea.Health"), ex);
            return Results.Json(new { status = "database unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [LoggerMessage(EventId = 1201, Level = LogLevel.Warning, Message = "Readiness check failed: database unavailable")]
    private static partial void LogNotReady(ILogger logger, Exception exception);
}
