using System.Text.RegularExpressions;

namespace Ardea.Assets.Persistence;

internal static partial class VersionPrecisions
{
    public const string None = "none";
    public const string Line = "line";
    public const string Exact = "exact";

    public static readonly string[] All = [None, Line, Exact];

    public static string Of(string? version, string? purl, string origin) =>
        string.IsNullOrWhiteSpace(version) ? None
        : purl is not null ? Exact
        : origin == "repository" && RepositoryLine().IsMatch(version.Trim()) ? Line
        : ManualLine().IsMatch(version.Trim()) ? Line
        : Exact;

    public static bool Fits(string precision, string? version) => (precision == None) == string.IsNullOrWhiteSpace(version);

    [GeneratedRegex(@"^\d+(\.\d+)?\z")]
    private static partial Regex RepositoryLine();

    [GeneratedRegex(@"^(\d+|\d+(\.\d+)*\.[xX*])\z")]
    private static partial Regex ManualLine();
}

internal static class NoticeThresholds
{
    public const string OnlyImportant = "only_important";
    public const string Everything = "everything";

    public static readonly string[] All = [OnlyImportant, Everything];
}
