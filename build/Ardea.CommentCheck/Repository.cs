using System.Diagnostics;

namespace Ardea.CommentCheck;

internal static class Repository
{
    public static string FindRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ardea.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (Ardea.slnx) not found; pass --root.");
    }

    public static IReadOnlyList<string> Files(string root)
    {
        var start = new ProcessStartInfo("git", "ls-files -z --cached --others --exclude-standard")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var git = Process.Start(start) ?? throw new InvalidOperationException("git could not be started.");
        var output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        if (git.ExitCode != 0)
        {
            throw new InvalidOperationException($"git ls-files failed with exit code {git.ExitCode}.");
        }

        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => File.Exists(Path.Combine(root, path)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
