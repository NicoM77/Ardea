using System.Reflection;

namespace Ardea.Host;

internal static class AppVersion
{
    public const string Development = "dev";

    public static string Current { get; } =
        FromInformationalVersion(typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    internal static string FromInformationalVersion(string? informationalVersion)
    {
        var version = informationalVersion?.Split('+')[0].Trim();
        return string.IsNullOrEmpty(version) || version == "0.0.0" ? Development : version;
    }
}
