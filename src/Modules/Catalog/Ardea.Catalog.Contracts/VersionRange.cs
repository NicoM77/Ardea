using System.Globalization;

namespace Ardea.Catalog.Contracts;

public enum VersionStatus
{
    Unknown,
    Affected,
    Unaffected,
}

public sealed record VersionRange
{
    public string? Introduced { get; init; }

    public string? Fixed { get; init; }

    public string? LastAffected { get; init; }

    public string? Exact { get; init; }

    public VersionStatus Status { get; init; } = VersionStatus.Affected;

    public string? Scheme { get; init; }

    public bool IsCommitRange =>
        string.Equals(Scheme, VersionSchemeNames.Git, StringComparison.OrdinalIgnoreCase)
        && (Bounds.Any(VersionRanges.IsCommitHash) || Bounds.All(VersionRanges.IsOpenLowerBound));

    public bool NamesNoVersion => Exact is not null && Introduced is null && Fixed is null && LastAffected is null && VersionRanges.IsOpenLowerBound(Exact);

    private IEnumerable<string?> Bounds => [Introduced, Fixed, LastAffected, Exact];

    public bool HasExplicitLowerBound => Exact is not null || Introduced is not null;

    public static VersionRange ExactVersion(string version, VersionStatus status = VersionStatus.Affected, string? scheme = null) =>
        new() { Exact = version, Status = status, Scheme = scheme };
}

public static class VersionRanges
{
    private static readonly HashSet<string> OpenWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "*", "-", "x", "n/a", "na", "unspecified", "unknown", "all", "any",
    };

    public static bool IsCommitHash(string? text)
    {
        var trimmed = text?.Trim();
        return trimmed is { Length: >= 7 and <= 40 } && trimmed.All(char.IsAsciiHexDigit);
    }

    public static bool IsOpenLowerBound(string? text) => text is null || OpenWords.Contains(text.Trim()) || text.Trim() == "0";

    public static bool IsOpenUpperBound(string? text) => text is null || OpenWords.Contains(text.Trim());

    public static bool TryExpandWildcard(string? text, out string lower, out string upperExclusive)
    {
        lower = upperExclusive = string.Empty;
        var trimmed = text?.Trim();
        if (trimmed is null || trimmed.Length < 3)
        {
            return false;
        }

        string prefix;
        if (trimmed.EndsWith(".*", StringComparison.Ordinal) || trimmed.EndsWith(".x", StringComparison.OrdinalIgnoreCase))
        {
            prefix = trimmed[..^2];
        }
        else
        {
            return false;
        }

        var parts = prefix.TrimStart('v', 'V').Split('.');
        if (parts.Any(p => p.Length is 0 or > 18 || !p.All(char.IsAsciiDigit)) || parts.Length > 6)
        {
            return false;
        }

        var last = long.Parse(parts[^1], CultureInfo.InvariantCulture);
        lower = prefix;
        upperExclusive = string.Join('.', parts[..^1].Append((last + 1).ToString(CultureInfo.InvariantCulture)));
        return true;
    }

    public static bool TrySplitRange(string? text, out string from, out string to)
    {
        from = to = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var separator in new[] { " - ", " – ", " to ", " through ", "-", "–" })
        {
            var index = text.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (index <= 0)
            {
                continue;
            }

            var left = text[..index].Trim();
            var right = text[(index + separator.Length)..].Trim();
            if (NumericParts(left) is { } l && NumericParts(right) is { } r && l.Length == r.Length && l.Length >= 2
                && CompareNumeric(l, r) <= 0)
            {
                from = left;
                to = right;
                return true;
            }
        }

        return false;
    }

    public static VersionRange FromCve(string? version, string? lessThan, string? lessThanOrEqual, VersionStatus status, string? versionType = null)
    {
        var range = new VersionRange { Status = status, Scheme = string.IsNullOrWhiteSpace(versionType) ? null : versionType.Trim() };
        return lessThan is not null || lessThanOrEqual is not null
            ? Normalize(range with { Introduced = version, Fixed = lessThan, LastAffected = lessThan is null ? lessThanOrEqual : null })
            : FromSingleField(version, status, range.Scheme);
    }

    public static VersionRange Normalize(VersionRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        var splitAllowed = !string.Equals(range.Scheme, VersionSchemeNames.Rpm, StringComparison.OrdinalIgnoreCase);
        if (range.Exact is { } exact)
        {
            return range.Introduced is null && range.Fixed is null && range.LastAffected is null && !OpenWords.Contains(exact.Trim())
                ? FromSingleField(exact, range.Status, range.Scheme)
                : range;
        }

        var introduced = Lower(range.Introduced, splitAllowed);
        string? fixedVersion = null, lastAffected = null;
        if (UpperText(range.Fixed) is { } lessThan)
        {
            fixedVersion = TryExpandWildcard(lessThan, out var lineStart, out var lineEnd)
                ? PlaceholderEnd(introduced, lessThan, lineStart, lineEnd, range.Status)
                : lessThan;
        }

        if (UpperText(range.LastAffected) is { } upTo)
        {
            if (TryExpandWildcard(upTo, out _, out var after))
            {
                fixedVersion ??= after;
            }
            else
            {
                lastAffected = upTo;
            }
        }

        return range with { Introduced = introduced, Fixed = fixedVersion, LastAffected = lastAffected };
    }

    public static VersionRange FromSingleField(string? version, VersionStatus status, string? scheme = null)
    {
        var range = new VersionRange { Status = status, Scheme = scheme };
        var text = version?.Trim();
        if (text is null || OpenWords.Contains(text))
        {
            return range;
        }

        if (TryOperator(text, out var op, out var operand))
        {
            return op switch
            {
                "<" => range with { Fixed = operand },
                "<=" => range with { LastAffected = operand },
                _ => range with { Introduced = operand },
            };
        }

        if (TryExpandWildcard(text, out var lower, out var upper))
        {
            return range with { Introduced = lower, Fixed = upper };
        }

        if (!string.Equals(scheme, VersionSchemeNames.Rpm, StringComparison.OrdinalIgnoreCase) && TrySplitRange(text, out var from, out var to))
        {
            return range with { Introduced = from, LastAffected = to };
        }

        return range with { Exact = text };
    }

    public static IReadOnlyList<VersionRange> FromOsvEvents(IEnumerable<KeyValuePair<string, string>> events, string? scheme = null)
    {
        var ranges = new List<VersionRange>();
        VersionRange? open = null;
        foreach (var (kind, value) in events)
        {
            switch (kind)
            {
                case "introduced":
                    if (open is not null)
                    {
                        ranges.Add(open);
                    }

                    open = new VersionRange { Introduced = IsOpenLowerBound(value) ? null : value, Scheme = scheme };
                    break;
                case "fixed":
                    ranges.Add((open ?? new VersionRange { Scheme = scheme }) with { Fixed = value });
                    open = null;
                    break;
                case "last_affected":
                    ranges.Add((open ?? new VersionRange { Scheme = scheme }) with { LastAffected = value });
                    open = null;
                    break;
                default:
                    break;
            }
        }

        if (open is not null)
        {
            ranges.Add(open);
        }

        return ranges;
    }

    private static string? Lower(string? version, bool splitAllowed)
    {
        if (IsOpenLowerBound(version))
        {
            return null;
        }

        var text = version!.Trim();
        if (TryExpandWildcard(text, out var lower, out _))
        {
            return lower;
        }

        if (TryOperator(text, out var op, out var operand) && op == ">=")
        {
            return operand;
        }

        return splitAllowed && TrySplitRange(text, out var from, out _) ? from : text;
    }

    private static string PlaceholderEnd(string? introduced, string written, string lineStart, string lineEnd, VersionStatus status)
    {
        var narrow = status == VersionStatus.Unaffected;
        if (introduced is null)
        {
            return narrow ? lineStart : lineEnd;
        }

        var lower = VersionLines.LeadingNumbers(introduced);
        if (lower.Length == 0)
        {
            return written;
        }

        var start = VersionLines.LeadingNumbers(lineStart);
        if (VersionLines.InLine(introduced, start))
        {
            return lineEnd;
        }

        return VersionLines.CompareNumbers(lower, start) < 0 && !narrow ? lineEnd : lineStart;
    }

    private static string? UpperText(string? text)
    {
        if (IsOpenUpperBound(text))
        {
            return null;
        }

        var trimmed = text!.Trim();
        return trimmed.Contains('*', StringComparison.Ordinal) && !TryExpandWildcard(trimmed, out _, out _) ? null : trimmed;
    }

    private static bool TryOperator(string text, out string op, out string operand)
    {
        foreach (var candidate in new[] { "<=", "≤", ">=", "≥", "<" })
        {
            if (text.StartsWith(candidate, StringComparison.Ordinal) && text.Length > candidate.Length)
            {
                op = candidate switch { "≤" => "<=", "≥" => ">=", _ => candidate };
                operand = text[candidate.Length..].Trim();
                return operand.Length > 0 && char.IsAsciiDigit(operand.TrimStart('v', 'V').FirstOrDefault());
            }
        }

        op = operand = string.Empty;
        return false;
    }

    private static long[]? NumericParts(string text)
    {
        var parts = text.Split('.');
        var numbers = new long[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length is 0 or > 18 || !parts[i].All(char.IsAsciiDigit))
            {
                return null;
            }

            numbers[i] = long.Parse(parts[i], CultureInfo.InvariantCulture);
        }

        return numbers;
    }

    private static int CompareNumeric(long[] left, long[] right)
    {
        for (var i = 0; i < left.Length; i++)
        {
            var c = left[i].CompareTo(right[i]);
            if (c != 0)
            {
                return c;
            }
        }

        return 0;
    }
}
