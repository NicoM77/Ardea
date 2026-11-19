using Microsoft.Extensions.Configuration;

namespace Ardea.Platform.Persistence;

/// <summary>
/// Where the master data files live that <c>migrate</c> loads after the migrations (master plan 4.11):
/// catalogue, sources, rule set. The image carries them in <c>/app/data</c>; <c>ARDEA_DATA_DIR</c>
/// points elsewhere. In a source checkout the repository's <c>data/</c> folder is found by itself.
/// </summary>
public sealed record MasterDataOptions(string Directory)
{
    public const string DirectoryKey = "DATA_DIR";

    public static MasterDataOptions FromConfiguration(IConfiguration configuration) =>
        new(configuration[DirectoryKey] is { Length: > 0 } directory ? directory : FindDefault());

    /// <summary>Next to the program, else the <c>data/</c> folder of the repository the program was built in.</summary>
    public static string FindDefault() => FindDefault(AppContext.BaseDirectory);

    /// <summary>
    /// A folder counts only if it holds the catalogue: on a case-insensitive file system a test project's fixture folder
    /// <c>Data/</c> next to the program is the same path as <c>data/</c>.
    /// </summary>
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
