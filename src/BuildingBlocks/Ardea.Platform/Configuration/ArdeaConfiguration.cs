using Microsoft.Extensions.Configuration;

namespace Ardea.Platform.Configuration;

/// <summary>Configuration comes from environment variables prefixed <c>ARDEA_</c> (master plan 12.4).</summary>
public static class ArdeaConfiguration
{
    public const string Prefix = "ARDEA_";

    public static IConfigurationBuilder AddArdeaEnvironment(this IConfigurationBuilder builder)
    {
        builder.AddEnvironmentVariables(Prefix);
        builder.Add(new SecretFileConfigurationSource(Prefix, Environment.GetEnvironmentVariables));
        return builder;
    }

    /// <summary>The environment variable that sets a configuration key, for error messages.</summary>
    public static string VariableName(string key) =>
        Prefix + key.Replace(ConfigurationPath.KeyDelimiter, "__", StringComparison.Ordinal).ToUpperInvariant();
}
