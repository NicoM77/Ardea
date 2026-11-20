using Ardea.Assets.Contracts;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Contracts;

namespace Ardea.Assessment.Matching;

internal static class VersionVerdicts
{
    public const string InRange = "in_range";
    public const string OutOfRange = "out_of_range";
    public const string Unknown = "unknown";
}

internal sealed record VersionStatement(Guid Source, string? Scheme, string Verdict, string Reason, string DefaultStatus, IReadOnlyList<RangeView> Ranges);

internal sealed record RangeView(string? Introduced, string? Fixed, string? LastAffected, string? Exact, string Status);

internal sealed record VersionResult(string Verdict, string Reason, string? FixedVersion, IReadOnlyList<string> FixedVersions, IReadOnlyList<VersionStatement> Statements);

internal static class VersionMatch
{
    private static readonly HashSet<string> GenericSchemes = new(StringComparer.OrdinalIgnoreCase) { "custom", "other", "string", "numeric" };

    public static VersionResult Check(
        ProfileComponent component, string matchType, IReadOnlyList<AffectedFacts> affected, IVersionSchemes schemes, IReadOnlyCollection<string>? fixesInText = null)
    {
        var statements = new List<VersionStatement>();
        var fixedVersions = new List<(string Text, ParsedVersion? Parsed)>();
        var ownFixes = new List<(VersionStatement Statement, string Text, ParsedVersion Parsed)>();
        foreach (var group in affected.GroupBy(a => (a.SourceId, a.ProductKey, a.CpeVendor, a.CpeProduct, a.PurlType, a.PurlName, a.Vendor, a.Product, a.DefaultStatus)))
        {
            var rows = group.ToList();
            var ranges = rows.Select(Range).ToList();
            var scheme = SchemeFor(component, matchType, rows, schemes);
            var defaultStatus = Status(group.Key.DefaultStatus);
            var check = VersionCheck.Evaluate(scheme, component.Version, ranges, defaultStatus, fixesInText);
            var statement = new VersionStatement(
                group.Key.SourceId, scheme?.Name, Verdict(check.Verdict), Snake(check.Reason.ToString()), group.Key.DefaultStatus,
                [.. rows.Select(r => new RangeView(r.Introduced, r.Fixed, r.LastAffected, r.Exact, r.Status))]);
            statements.Add(statement);

            foreach (var range in ranges.Where(r => r.Status == VersionStatus.Affected && r.Fixed is not null))
            {
                ParsedVersion? parsed = null;
                if (scheme is not null && scheme.TryParse(range.Fixed, out var p))
                {
                    parsed = p;
                }

                if (!fixedVersions.Exists(f => f.Text == range.Fixed))
                {
                    fixedVersions.Add((range.Fixed!, parsed));
                }

                if (check.Verdict == VersionVerdict.InRange && parsed is not null
                    && VersionCheck.Evaluate(scheme, component.Version, [range], VersionStatus.Unknown).Verdict == VersionVerdict.InRange)
                {
                    ownFixes.Add((statement, range.Fixed!, parsed));
                }
            }
        }

        var overruled = OverruledEmptyRanges(component.Version, statements);
        var deciding = statements.Where(s => !overruled.Contains(s)).ToList();
        string? fixedVersion = null;
        ParsedVersion? fixedParsed = null;
        foreach (var (_, text, parsed) in ownFixes.Where(f => !overruled.Contains(f.Statement)))
        {
            if (fixedParsed is null || (parsed.Scheme == fixedParsed.Scheme && parsed > fixedParsed))
            {
                fixedVersion = text;
                fixedParsed = parsed;
            }
        }

        var ordered = fixedVersions.Select(f => f.Text).ToList();
        if (string.Equals(component.Packaging, "distribution", StringComparison.Ordinal))
        {
            return new VersionResult(VersionVerdicts.Unknown, "distribution_package", null, ordered, statements);
        }

        if (deciding.Find(s => s.Verdict == VersionVerdicts.InRange) is { } inRange)
        {
            return new VersionResult(VersionVerdicts.InRange, inRange.Reason, fixedVersion, ordered, statements);
        }

        var silent = Snake(nameof(VersionCheckReason.NoComparableRange));
        if (deciding.Find(s => s.Verdict == VersionVerdicts.Unknown && s.Reason != silent) is { } undecided)
        {
            return new VersionResult(VersionVerdicts.Unknown, undecided.Reason, null, ordered, statements);
        }

        if (deciding.Find(s => s.Verdict == VersionVerdicts.OutOfRange) is { } outside)
        {
            return new VersionResult(VersionVerdicts.OutOfRange, outside.Reason, null, ordered, statements);
        }

        return new VersionResult(VersionVerdicts.Unknown, deciding.Count == 0 ? silent : deciding[0].Reason, null, ordered, statements);
    }

    private static HashSet<VersionStatement> OverruledEmptyRanges(string? installed, List<VersionStatement> statements)
    {
        var line = VersionLines.Line(installed);
        var empty = Snake(nameof(VersionCheckReason.BeforeEmptyRange));
        var ownLineDecided = line.Length > 0 && statements.Exists(s => s.Verdict == VersionVerdicts.OutOfRange
            && s.Ranges.Any(r => r.Status == "affected" && (r.Introduced ?? r.Exact) is { } lower && VersionLines.InLine(lower, line)));
        return new HashSet<VersionStatement>(ownLineDecided ? statements.Where(s => s.Reason == empty) : [], ReferenceEqualityComparer.Instance);
    }

    private static IVersionScheme? SchemeFor(ProfileComponent component, string matchType, IReadOnlyList<AffectedFacts> rows, IVersionSchemes schemes)
    {
        if (matchType == MatchTypes.Package && rows.Select(r => r.PurlType).FirstOrDefault(t => t is not null) is { } ecosystem)
        {
            return schemes.ForEcosystem(ecosystem);
        }

        var candidates = new List<IVersionScheme?>();
        var recordScheme = rows.Select(r => r.VersionScheme).FirstOrDefault(s => s is not null && !string.Equals(s, VersionSchemeNames.Git, StringComparison.OrdinalIgnoreCase));
        if (recordScheme is not null && !GenericSchemes.Contains(recordScheme))
        {
            candidates.Add(schemes.Find(recordScheme));
        }

        candidates.Add(component.VersionScheme is null ? null : schemes.Find(component.VersionScheme));
        candidates.Add(schemes.Find(VersionSchemeNames.Numeric));
        var usable = candidates.OfType<IVersionScheme>().ToList();
        var installed = component.Version?.Trim();
        var bounds = rows.Select(Range).Where(r => !r.IsCommitRange && !r.NamesNoVersion)
            .SelectMany(r => new[] { r.Introduced, r.Fixed, r.LastAffected, r.Exact })
            .OfType<string>()
            .ToList();
        return usable.Find(s => installed is not null && s.IsVersion(installed) && bounds.TrueForAll(b => s.IsVersion(b)))
            ?? usable.Find(s => installed is not null && s.IsVersion(installed))
            ?? usable.FirstOrDefault();
    }

    private static VersionRange Range(AffectedFacts row) => new()
    {
        Introduced = row.Introduced,
        Fixed = row.Fixed,
        LastAffected = row.LastAffected,
        Exact = row.Exact,
        Status = Status(row.Status),
        Scheme = row.VersionScheme,
    };

    private static VersionStatus Status(string? status) => status switch
    {
        "affected" => VersionStatus.Affected,
        "unaffected" => VersionStatus.Unaffected,
        _ => VersionStatus.Unknown,
    };

    private static string Verdict(VersionVerdict verdict) => verdict switch
    {
        VersionVerdict.InRange => VersionVerdicts.InRange,
        VersionVerdict.OutOfRange => VersionVerdicts.OutOfRange,
        _ => VersionVerdicts.Unknown,
    };

    internal static string Snake(string name) =>
        string.Concat(name.Select((c, i) => char.IsUpper(c) ? (i > 0 ? "_" : string.Empty) + char.ToLowerInvariant(c) : c.ToString()));
}
