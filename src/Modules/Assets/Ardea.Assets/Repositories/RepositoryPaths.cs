using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Ardea.Assets.Repositories;

/// <summary>
/// Paths, names and refs of a repository (master plan 16.9): they are checked and compared as text,
/// never resolved against a file system. A path is relative to the repository root, separated by
/// <c>/</c>, without empty, <c>.</c> or <c>..</c> segments, backslashes or control characters.
/// </summary>
internal static partial class RepositoryPaths
{
    private const int MaxPathLength = 4096;

    /// <summary>Whether <paramref name="path"/> is a well-formed relative path of a file in the repository.</summary>
    public static bool IsValidPath([NotNullWhen(true)] string? path) =>
        path is { Length: > 0 and <= MaxPathLength }
        && !path.Contains('\\', StringComparison.Ordinal)
        && !path.Any(char.IsControl)
        && path.Split('/').All(s => s.Length > 0 && s != "." && s != "..");

    /// <summary>
    /// The subfolder of a link in its comparable form: without leading and trailing slashes; <c>null</c>
    /// for the whole repository. <c>false</c> when it is malformed.
    /// </summary>
    public static bool TryNormalizeFolder(string? folder, out string? normalized)
    {
        normalized = null;
        var trimmed = folder?.Trim().Trim('/');
        if (string.IsNullOrEmpty(trimmed))
        {
            return true;
        }

        if (!IsValidPath(trimmed))
        {
            return false;
        }

        normalized = trimmed;
        return true;
    }

    /// <summary>Whether <paramref name="path"/> lies in <paramref name="folder"/> (normalised; <c>null</c> is the root). Case-sensitive, like Git.</summary>
    public static bool IsWithin(string path, string? folder) =>
        folder is null
        || (path.StartsWith(folder, StringComparison.Ordinal) && (path.Length == folder.Length || path[folder.Length] == '/'));

    /// <summary>The path for a URL: every segment escaped, the slashes kept.</summary>
    public static string Escape(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    /// <summary>Owner and repository names as GitHub allows them.</summary>
    public static bool IsValidName([NotNullWhen(true)] string? name) =>
        name is { Length: > 0 and <= 100 } && name != "." && name != ".." && NamePattern().IsMatch(name);

    /// <summary>A branch name per <c>git check-ref-format</c>, roughly: no space, <c>~ ^ : ? * [ \</c>, <c>..</c> or <c>@{</c>.</summary>
    public static bool IsValidRef([NotNullWhen(true)] string? reference) =>
        reference is { Length: > 0 and <= 255 }
        && !reference.Any(c => char.IsControl(c) || c == ' ' || "~^:?*[\\".Contains(c, StringComparison.Ordinal))
        && !reference.Contains("..", StringComparison.Ordinal)
        && !reference.Contains("@{", StringComparison.Ordinal)
        && !reference.Contains("//", StringComparison.Ordinal)
        && !reference.StartsWith('/') && !reference.EndsWith('/') && !reference.EndsWith('.');

    /// <summary>A full commit SHA: 40 (SHA-1) or 64 (SHA-256) hex digits.</summary>
    public static bool IsCommitSha([NotNullWhen(true)] string? sha) => sha is not null && ShaPattern().IsMatch(sha);

    [GeneratedRegex("^[A-Za-z0-9_.-]+$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$")]
    private static partial Regex ShaPattern();
}
