namespace Ardea.Sources.Tests;

internal static class TestData
{
    public static string Path(string source, string file) => System.IO.Path.Combine(AppContext.BaseDirectory, "Data", source, file);

    public static string Read(string source, string file) => File.ReadAllText(Path(source, file));

    public static IEnumerable<string> Files(string source, string pattern) =>
        Directory.GetFiles(System.IO.Path.Combine(AppContext.BaseDirectory, "Data", source), pattern).Select(System.IO.Path.GetFileName).Order(StringComparer.Ordinal)!;
}
