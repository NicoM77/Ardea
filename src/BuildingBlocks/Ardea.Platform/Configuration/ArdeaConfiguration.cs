using Microsoft.Extensions.Configuration;

namespace Ardea.Platform.Configuration;

public static class ArdeaConfiguration
{
    public const string Prefix = "ARDEA_";

    public static IConfigurationBuilder AddArdeaEnvironment(this IConfigurationBuilder builder)
    {
        builder.AddEnvironmentVariables(Prefix);
        builder.Add(new SecretFileConfigurationSource(Prefix, Environment.GetEnvironmentVariables));
        return builder;
    }

    public static string VariableName(string key) =>
        Prefix + key.Replace(ConfigurationPath.KeyDelimiter, "__", StringComparison.Ordinal).ToUpperInvariant();
}
