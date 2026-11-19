using System.Globalization;

namespace Ardea.Catalog.Contracts;

/// <summary>What a record says about a range of versions (<c>entry_affected.status</c>).</summary>
public enum VersionStatus
{
    /// <summary>The record says nothing definite; also the default when a record names no default status.</summary>
    Unknown,
    Affected,
    Unaffected,
}

/// <summary>
/// One version range of an affected product, as in <c>intel.entry_affected</c> (master plan 4.4):
/// either an <see cref="Exact"/> version, or a range from <see cref="Introduced"/> (inclusive; <c>null</c>
/// when the record names no lower bound) up to <see cref="Fixed"/> (exclusive) or
/// <see cref="LastAffected"/> (inclusive); without either it is open upwards. Bounds are kept as the
/// record writes them, after the normalisation of <see cref="VersionRanges"/>.
/// </summary>
public sealed record VersionRange
{
    public string? Introduced { get; init; }

    public string? Fixed { get; init; }

    public string? LastAffected { get; init; }

    public string? Exact { get; init; }

    public VersionStatus Status { get; init; } = VersionStatus.Affected;

    /// <summary>The version type the record names for this range, if any; <c>git</c> ranges are never evaluated.</summary>
    public string? Scheme { get; init; }

    /// <summary>Whether the record names a lower bound: an exact version, or a non-open <see cref="Introduced"/>.</summary>
    public bool HasExplicitLowerBound => Exact is not null || Introduced is not null;

    public static VersionRange ExactVersion(string version, VersionStatus status = VersionStatus.Affected, string? scheme = null) =>
        new() { Exact = version, Status = status, Scheme = scheme };
}

/// <summary>
/// Turns version statements of records into <see cref="VersionRange"/>s and resolves the known
/// peculiarities (master plan 6.6): placeholders like <c>8.2.*</c>, ranges in one field like
/// <c>8.4.0-8.4.13</c>, open bounds like <c>0</c> and <c>*</c>, and comparison prefixes like <c>&lt;= 2.1</c>.
/// </summary>
public static class VersionRanges
{
    private static readonly HashSet<string> OpenWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "*", "-", "x", "n/a", "na", "unspecified", "unknown", "all", "any",
    };

    /// <summary>Whether a lower bound means «from the beginning»: missing, <c>0</c>, <c>*</c>, <c>unspecified</c>, ….</summary>
    public static bool IsOpenLowerBound(string? text) => text is null || OpenWords.Contains(text.Trim()) || text.Trim() == "0";

    /// <summary>Whether an upper bound means «no end»: missing, <c>*</c>, <c>unspecified</c>, ….</summary>
    public static bool IsOpenUpperBound(string? text) => text is null || OpenWords.Contains(text.Trim());

    /// <summary>
    /// Expands a placeholder version like <c>8.2.*</c> or <c>8.x</c> into its lowest version
    /// (<c>8.2</c>) and the exclusive bound after it (<c>8.3</c>).
    /// </summary>
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
        if (parts.Any(p => p.Length == 0 || !p.All(char.IsAsciiDigit)) || parts.Length > 6)
        {
            return false;
        }

        var last = long.Parse(parts[^1], CultureInfo.InvariantCulture);
        lower = prefix;
        upperExclusive = string.Join('.', parts[..^1].Append((last + 1).ToString(CultureInfo.InvariantCulture)));
        return true;
    }

    /// <summary>
    /// Splits a range written in one field, <c>8.4.0-8.4.13</c> or <c>8.4.0 - 8.4.13</c>, into its two
    /// ends. Only numeric dot sequences with the same number of parts (at least two) count, so pre-release
    /// versions (<c>1.0.0-rc.1</c>) and RPM releases (<c>1.2-4</c>) are not split.
    /// </summary>
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

    /// <summary>
    /// One version statement of a CVE record (CVE JSON 5: <c>version</c>, <c>lessThan</c>,
    /// <c>lessThanOrEqual</c>, <c>status</c>, <c>versionType</c>) as a range.
    /// </summary>
    public static VersionRange FromCve(string? version, string? lessThan, string? lessThanOrEqual, VersionStatus status, string? versionType = null)
    {
        var range = new VersionRange { Status = status, Scheme = string.IsNullOrWhiteSpace(versionType) ? null : versionType.Trim() };
        var splitAllowed = !string.Equals(range.Scheme, VersionSchemeNames.Rpm, StringComparison.OrdinalIgnoreCase);

        if (lessThan is not null || lessThanOrEqual is not null)
        {
            range = range with { Introduced = Lower(version, splitAllowed) };
            if (lessThan is not null)
            {
                return IsOpenUpperBound(lessThan) ? range
                    : TryExpandWildcard(lessThan, out var below, out _) ? range with { Fixed = below }
                    : range with { Fixed = lessThan.Trim() };
            }

            return IsOpenUpperBound(lessThanOrEqual) ? range
                : TryExpandWildcard(lessThanOrEqual, out _, out var after) ? range with { Fixed = after }
                : range with { LastAffected = lessThanOrEqual!.Trim() };
        }

        return FromSingleField(version, status, range.Scheme);
    }

    /// <summary>
    /// Resolves the known peculiarities (master plan 6.6) in a range a source reader has already split into
    /// its fields, as <c>intel.entry_affected</c> stores it:
    /// <list type="bullet">
    /// <item>an exact version that is itself a range (<c>8.4.0-8.4.13</c>, <c>8.2.*</c>, <c>&lt; 2.1</c>) becomes that range;
    /// an open word alone (<c>*</c>, <c>unspecified</c>, <c>n/a</c>) stays as written, because most publishers mean «not stated» by it, not «every version»;</item>
    /// <item>an open lower bound (<c>0</c>, <c>*</c>, <c>unspecified</c>) becomes none, a placeholder lower bound its lowest version;</item>
    /// <item>an upper bound with a placeholder (<c>lessThan 6.12.*</c>, <c>lessThanOrEqual 6.12.*</c>) ends before or after
    /// that line; any other upper bound with a wildcard (<c>*</c>, <c>log4j-core*</c>) names no end.</item>
    /// </list>
    /// Applying it twice gives the same range.
    /// </summary>
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
            fixedVersion = TryExpandWildcard(lessThan, out var below, out _) ? below : lessThan;
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

    /// <summary>
    /// A single version field that may itself be a range: <c>8.2.*</c>, <c>8.4.0-8.4.13</c>,
    /// <c>&lt; 2.1</c>, <c>&lt;= 2.1</c>, <c>&gt;= 2.1</c>; anything else is an exact version.
    /// <c>*</c> or <c>unspecified</c> alone stands for all versions.
    /// </summary>
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

    /// <summary>
    /// The events of an OSV range (<c>introduced</c>, <c>fixed</c>, <c>last_affected</c>, <c>limit</c>),
    /// in record order, as affected ranges. Each <c>introduced</c> opens a range; the next
    /// <c>fixed</c> or <c>last_affected</c> closes it. <c>introduced: "0"</c> has no lower bound.
    /// </summary>
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
                    // «limit» only bounds the search in a git history; it says nothing about affectedness.
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

    /// <summary>An upper bound as text, or <c>null</c> when it names no end: missing, an open word, or a wildcard that is no placeholder version.</summary>
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
