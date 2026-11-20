using Ardea.Platform.Configuration;
using Ardea.Platform.Persistence;

namespace Ardea.Host;

internal static class HostConfiguration
{
    public const string MasterKeyKey = "MASTER_KEY";
    public const string PublicUrlKey = "PUBLIC_URL";

    public static IConfigurationRoot Load()
    {
        var builder = new ConfigurationBuilder();
        builder.AddArdeaEnvironment();
        return builder.Build();
    }

    public static ConfigurationCheck CheckApplication(IConfiguration configuration) =>
        new ConfigurationCheck(configuration)
            .Require(DatabaseOptions.AppPasswordKey, "password of the application role for API and worker")
            .RequireBase64Key(MasterKeyKey, 32, "master key that protects stored secrets; generate one with `ardea init-env`")
            .RequireAbsoluteUrl(PublicUrlKey, "public address of the instance, used for links and cookies")
            .OneOf("REGISTRATION", "invite", "open", "closed")
            .PositiveInt("DB_PORT")
            .PositiveInt(Ardea.Platform.Ai.AiLimits.ParallelismKey)
            .PositiveInt(Ardea.Platform.Ai.AiLimits.CallTimeoutKey)
            .PositiveInt(Ardea.Platform.Ai.AiLimits.DailyTokensKey)
            .PositiveInt(Ardea.Platform.Ai.AiLimits.DailyTokensPerOrganizationKey)
            .PositiveInt(Ardea.Platform.Ai.AiLimits.DailyRequestsPerOrganizationKey)
            .PositiveInt("AI:CATALOG_DAILY_CALLS")
            .Add(AiErrors(configuration))
            .Add(Ardea.Notifications.NotificationsModule.ConfigurationErrors(configuration));

    private static IReadOnlyList<string> AiErrors(IConfiguration configuration)
    {
        var providers = Ardea.Platform.Ai.AiProviderOptions.FromConfiguration(configuration, out var errors);

        if (configuration["AI:CATALOG_PROVIDER"]?.Trim() is { Length: > 0 } catalog && catalog != "none" && errors.Count == 0
            && !providers.Any(p => string.Equals(p.Name, catalog, StringComparison.Ordinal)))
        {
            return [.. errors, "ARDEA_AI__CATALOG_PROVIDER must name a configured provider (ARDEA_AI__PROVIDERS__<Name>__…) or be none."];
        }

        return errors;
    }

    public static ConfigurationCheck CheckMigration(IConfiguration configuration) =>
        new ConfigurationCheck(configuration)
            .Require(DatabaseOptions.OwnerPasswordKey, "password of the owner role, only for migrate and maintenance commands")
            .Require(DatabaseOptions.AppPasswordKey, "password of the application role that migrate creates")
            .PositiveInt("DB_PORT");

    public static int Fail(ConfigurationCheck check)
    {
        Console.Error.WriteLine("Ardea cannot start because of its configuration:");
        foreach (var error in check.Errors)
        {
            Console.Error.WriteLine($"  - {error}");
        }

        return 78;
    }

    public static void AddJsonLogging(ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        logging.AddJsonConsole(o =>
        {
            o.UseUtcTimestamp = true;
            o.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z' ";
            o.IncludeScopes = true;
        });
    }
}
