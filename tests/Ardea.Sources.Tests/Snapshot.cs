using System.Runtime.CompilerServices;

namespace Ardea.Sources.Tests;

/// <summary>
/// Reviewed snapshots of adapter output (master plan 13.6: "Ausgabe als Snapshot"). The expected
/// output lives next to the test as <c>*.verified.json</c>; a difference fails the test and writes
/// <c>*.received.json</c> beside it for review. <c>ARDEA_UPDATE_SNAPSHOTS=1</c> accepts the new output.
/// (Verify, the usual library, enforces a maintenance-fee licence since version 33; see master plan 2.6.)
/// </summary>
internal static class Snapshot
{
    public static void Match(string actual, string name, [CallerFilePath] string testFile = "")
    {
        var directory = Path.Combine(Path.GetDirectoryName(SourcePath(testFile))!, "Snapshots");
        Directory.CreateDirectory(directory);
        var verified = Path.Combine(directory, name + ".verified.json");
        var received = Path.Combine(directory, name + ".received.json");
        var normalized = actual.ReplaceLineEndings("\n").TrimEnd() + "\n";

        if (Environment.GetEnvironmentVariable("ARDEA_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(verified, normalized);
        }

        var expected = File.Exists(verified) ? File.ReadAllText(verified).ReplaceLineEndings("\n") : null;
        if (expected == normalized)
        {
            File.Delete(received);
            return;
        }

        File.WriteAllText(received, normalized);
        Assert.Fail(expected is null
            ? $"No snapshot {name} yet. Review {received} and accept it with ARDEA_UPDATE_SNAPSHOTS=1."
            : $"Output differs from snapshot {name}. Compare {received} with {verified}; accept intended changes with ARDEA_UPDATE_SNAPSHOTS=1.");
    }

    /// <summary>CI builds are deterministic and record source paths as <c>/_/…</c>; map them back to the checkout.</summary>
    private static string SourcePath(string compiledPath)
    {
        if (!compiledPath.StartsWith("/_/", StringComparison.Ordinal))
        {
            return compiledPath;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ardea.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), compiledPath[3..]);
    }
}
