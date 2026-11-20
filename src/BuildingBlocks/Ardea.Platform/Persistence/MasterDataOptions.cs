using Microsoft.Extensions.Configuration;

namespace Ardea.Platform.Persistence;

public sealed record MasterDataOptions(string Directory)
{
    public const string DirectoryKey = "DATA_DIR";

    public static MasterDataOptions FromConfiguration(IConfiguration configuration) =>
        new(configuration[DirectoryKey] is { Length: > 0 } directory ? directory : FindDefault());

    public static string FindDefault() => FindDefault(AppContext.BaseDirectory);

    internal static string FindDefault(string baseDirectory)
    {
        var beside = Path.Combine(baseDirectory, "data");
        if (HoldsMasterData(beside))
        {
            return beside;
        }

        for (var dir = new DirectoryInfo(baseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (File.Exists(Path.Combine(dir.FullName, "Ardea.slnx")) && HoldsMasterData(candidate))
            {
                return candidate;
            }
        }

        return beside;
    }

    private static bool HoldsMasterData(string directory) => System.IO.Directory.Exists(Path.Combine(directory, "catalog"));
}
