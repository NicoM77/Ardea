namespace Ardea.CommentCheck;

internal enum FileKind
{
    None,
    CSharp,
    Shell,
}

internal static class Scope
{
    private static readonly string[] CSharpRoots = ["src/", "tests/", "build/"];

    private static readonly string[] Generated = [".Designer.cs", "ModelSnapshot.cs"];

    private static readonly string[] TemporarilyExcluded =
    [
        "src/Modules/Notifications/",
        "tests/Ardea.Notifications.Tests/",
    ];

    private static readonly string[] OutOfScope = ["docs/", "node_modules/", "/node_modules/"];

    public static FileKind Classify(string path)
    {
        if (OutOfScope.Any(p => path.StartsWith(p, StringComparison.Ordinal) || path.Contains(p, StringComparison.Ordinal))
            || TemporarilyExcluded.Any(p => path.StartsWith(p, StringComparison.Ordinal)))
        {
            return FileKind.None;
        }

        if (path.EndsWith(".cs", StringComparison.Ordinal))
        {
            return CSharpRoots.Any(r => path.StartsWith(r, StringComparison.Ordinal)) && !Generated.Any(g => path.EndsWith(g, StringComparison.Ordinal))
                ? FileKind.CSharp
                : FileKind.None;
        }

        return path.EndsWith(".sh", StringComparison.Ordinal) ? FileKind.Shell : FileKind.None;
    }

    public static bool IsMigration(string path) => path.Contains("/Migrations/", StringComparison.Ordinal);
}
