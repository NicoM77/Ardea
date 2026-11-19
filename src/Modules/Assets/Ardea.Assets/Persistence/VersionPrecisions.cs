using System.Text.RegularExpressions;

namespace Ardea.Assets.Persistence;

/// <summary>
/// How precisely a component's version names what runs (master plan 17.1, ADR 046). The depth hangs on the component,
/// not on the project: <c>exact</c> goes the precise path (finding and action), <c>none</c> and <c>line</c> the coarse
/// one (notice).
/// </summary>
internal static partial class VersionPrecisions
{
    public const string None = "none";
    public const string Line = "line";
    public const string Exact = "exact";

    public static readonly string[] All = [None, Line, Exact];

    /// <summary>
    /// The precision a version has when nobody states it (ADR 046, also the backfill of existing components):
    /// <list type="number">
    /// <item>no version: <c>none</c>;</item>
    /// <item>a package (purl): <c>exact</c>, lock files and SBOMs name releases;</item>
    /// <item>a platform read from a repository with one or two numbers («10.0» from an image tag): <c>line</c>, as the
    /// coverage hint «version line only» already says;</item>
    /// <item>a version typed by hand with one number («17») or ending in a wildcard («8.x», «8.3.*»): <c>line</c>;</item>
    /// <item>anything else: <c>exact</c>. «8.3» typed by hand stays exact, because many products count releases with
    /// two numbers (PostgreSQL 17.11); whoever means the line says so.</item>
    /// </list>
    /// The migration <c>StackAndPrecision</c> applies the same rule in SQL.
    /// </summary>
    public static string Of(string? version, string? purl, string origin) =>
        string.IsNullOrWhiteSpace(version) ? None
        : purl is not null ? Exact
        : origin == "repository" && RepositoryLine().IsMatch(version) ? Line
        : ManualLine().IsMatch(version) ? Line
        : Exact;

    /// <summary>A stated precision must fit the version: <c>none</c> exactly when there is none.</summary>
    public static bool Fits(string precision, string? version) => (precision == None) == string.IsNullOrWhiteSpace(version);

    [GeneratedRegex(@"^\d+(\.\d+)?$")]
    private static partial Regex RepositoryLine();

    [GeneratedRegex(@"^(\d+|\d+(\.\d+)*\.[xX*])$")]
    private static partial Regex ManualLine();
}

/// <summary>When a coarse entry raises a notice (ADR 044 decisions 2 to 4).</summary>
internal static class NoticeThresholds
{
    public const string OnlyImportant = "only_important";
    public const string Everything = "everything";

    public static readonly string[] All = [OnlyImportant, Everything];
}
