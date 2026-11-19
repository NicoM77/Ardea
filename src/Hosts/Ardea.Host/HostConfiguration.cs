using Ardea.Platform.Configuration;
using Ardea.Platform.Persistence;

namespace Ardea.Host;

/// <summary>Configuration shared by every role: environment with prefix, JSON logs, start-up checks.</summary>
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

    /// <summary>What API and worker need (master plan 12.4).</summary>
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
            .Add(AiErrors(configuration));

    /// <summary>A provider that is configured wrongly stops the start instead of failing on its first call (master plan 7.8).</summary>
    private static IReadOnlyList<string> AiErrors(IConfiguration configuration)
    {
        Ardea.Platform.Ai.AiProviderOptions.FromConfiguration(configuration, out var errors);
        return errors;
    }

    /// <summary>What <c>migrate</c> needs.</summary>
    public static ConfigurationCheck CheckMigration(IConfiguration configuration) =>
        new ConfigurationCheck(configuration)
            .Require(DatabaseOptions.OwnerPasswordKey, "password of the owner role, only for migrate and maintenance commands")
            .Require(DatabaseOptions.AppPasswordKey, "password of the application role that migrate creates")
            .PositiveInt("DB_PORT");

    /// <summary>Writes every error and returns the exit code for invalid configuration.</summary>
    public static int Fail(ConfigurationCheck check)
    {
        Console.Error.WriteLine("Ardea cannot start because of its configuration:");
        foreach (var error in check.Errors)
        {
            Console.Error.WriteLine($"  - {error}");
        }

        return 78; // EX_CONFIG
    }

    public static void AddJsonLogging(ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddJsonConsole(o =>
        {
            o.UseUtcTimestamp = true;
            o.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z' ";
            o.IncludeScopes = true;
        });
    }
}
