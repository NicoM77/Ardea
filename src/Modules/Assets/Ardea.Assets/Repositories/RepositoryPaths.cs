using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Ardea.Assets.Repositories;

internal static partial class RepositoryPaths
{
    private const int MaxPathLength = 4096;

    public static bool IsValidPath([NotNullWhen(true)] string? path) =>
        path is { Length: > 0 and <= MaxPathLength }
        && !path.Contains('\\', StringComparison.Ordinal)
        && !path.Any(char.IsControl)
        && path.Split('/').All(s => s.Length > 0 && s != "." && s != "..");

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

    public static bool IsWithin(string path, string? folder) =>
        folder is null
        || (path.StartsWith(folder, StringComparison.Ordinal) && (path.Length == folder.Length || path[folder.Length] == '/'));

    public static string Escape(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    public static bool IsValidName([NotNullWhen(true)] string? name) =>
        name is { Length: > 0 and <= 100 } && name != "." && name != ".." && NamePattern().IsMatch(name);

    public static bool IsValidRef([NotNullWhen(true)] string? reference) =>
        reference is { Length: > 0 and <= 255 }
        && !reference.Any(c => char.IsControl(c) || c == ' ' || "~^:?*[\\".Contains(c, StringComparison.Ordinal))
        && !reference.Contains("..", StringComparison.Ordinal)
        && !reference.Contains("@{", StringComparison.Ordinal)
        && !reference.Contains("//", StringComparison.Ordinal)
        && !reference.StartsWith('/') && !reference.EndsWith('/') && !reference.EndsWith('.');

    public static bool IsCommitSha([NotNullWhen(true)] string? sha) => sha is not null && ShaPattern().IsMatch(sha);

    [GeneratedRegex("^[A-Za-z0-9_.-]+$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$")]
    private static partial Regex ShaPattern();
}
