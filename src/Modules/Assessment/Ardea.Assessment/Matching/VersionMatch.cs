using Ardea.Assets.Contracts;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Contracts;

namespace Ardea.Assessment.Matching;

/// <summary>The three outcomes of the version check as stored in <c>finding.version_verdict</c>.</summary>
internal static class VersionVerdicts
{
    public const string InRange = "in_range";
    public const string OutOfRange = "out_of_range";
    public const string Unknown = "unknown";
}

/// <summary>The version statements of one source about one product, checked as one record (master plan 6.6).</summary>
/// <param name="Source">The source that made the statements.</param>
/// <param name="Scheme">The scheme the versions were compared in, or <c>null</c> when none could.</param>
/// <param name="Verdict">One of <see cref="VersionVerdicts"/>.</param>
/// <param name="Reason">The rule that gave it, in snake case (<see cref="VersionCheckReason"/>).</param>
/// <param name="DefaultStatus">The record's status for versions it does not name.</param>
/// <param name="Ranges">The ranges as the record states them.</param>
internal sealed record VersionStatement(Guid Source, string? Scheme, string Verdict, string Reason, string DefaultStatus, IReadOnlyList<RangeView> Ranges);

/// <summary>A range as a record states it, for the evidence of a finding.</summary>
internal sealed record RangeView(string? Introduced, string? Fixed, string? LastAffected, string? Exact, string Status);

/// <summary>The result of the version check for one finding (master plan 6.6, 7.3, 7.4).</summary>
/// <param name="Verdict">One of <see cref="VersionVerdicts"/>.</param>
/// <param name="Reason">The rule that decided, in snake case.</param>
/// <param name="FixedVersion">The fix of the range the installed version lies in; the highest when several sources name one.</param>
/// <param name="FixedVersions">Every fix version the sources name, for a target when the installed version is unknown.</param>
/// <param name="Statements">The checks per source and product, as evidence.</param>
internal sealed record VersionResult(string Verdict, string Reason, string? FixedVersion, IReadOnlyList<string> FixedVersions, IReadOnlyList<VersionStatement> Statements);

/// <summary>
/// The version check of a strong hit (master plan 6.6, 7.3; ADR 033). Every source's statements about
/// the product are checked as one record with its default status. Then:
/// <list type="number">
/// <item>a component packaged by a distribution is unknown: distributions backport fixes without changing the upstream version;</item>
/// <item>one record that puts the version inside an affected range decides «in range»;</item>
/// <item>a record that cannot decide (unknown range status, unreadable bounds, gaps) keeps it «unknown»;</item>
/// <item>otherwise «out of range» when at least one record says so; records that name no comparable range at all do not veto it;</item>
/// <item>else «unknown».</item>
/// </list>
/// </summary>
internal static class VersionMatch
{
    private static readonly HashSet<string> GenericSchemes = new(StringComparer.OrdinalIgnoreCase) { "custom", "other", "string", "numeric" };

    /// <summary>Checks the installed version of a component against an entry's statements about the product.</summary>
    /// <param name="component">The component that was hit.</param>
    /// <param name="matchType">How it was hit (<see cref="MatchTypes"/>); a package hit compares in its ecosystem's scheme.</param>
    /// <param name="affected">The entry's statements that caused the hit.</param>
    /// <param name="schemes">The version schemes of the instance.</param>
    /// <param name="fixesInText">The versions the entry's text names as fixes (<see cref="EntryFacts.FixVersionsInText"/>).</param>
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

                // The fix that belongs to the installed version's own branch: the range that contains it.
                if (check.Verdict == VersionVerdict.InRange && parsed is not null
                    && VersionCheck.Evaluate(scheme, component.Version, [range], VersionStatus.Unknown).Verdict == VersionVerdict.InRange)
                {
                    ownFixes.Add((statement, range.Fixed!, parsed));
                }
            }
        }

        // Every statement stays in the evidence; the overruled ones do not decide.
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

    /// <summary>
    /// Statements that are «in range» only through an empty range read as «before its fix», while another statement
    /// of the entry names the installed version's own line and places the version out of range: then the empty range
    /// is about another line. Microsoft writes «Visual Studio 2026 version 18.12» as 18.12.0 to before 18.12.0, and its
    /// 18.11 statement fixes 18.11.9 at 18.11.4 (reference pair P055). Such statements do not decide; they stay in the evidence.
    /// </summary>
    private static HashSet<VersionStatement> OverruledEmptyRanges(string? installed, List<VersionStatement> statements)
    {
        var line = VersionLines.Line(installed);
        var empty = Snake(nameof(VersionCheckReason.BeforeEmptyRange));
        var ownLineDecided = line.Length > 0 && statements.Exists(s => s.Verdict == VersionVerdicts.OutOfRange
            && s.Ranges.Any(r => r.Status == "affected" && (r.Introduced ?? r.Exact) is { } lower && VersionLines.InLine(lower, line)));
        return new HashSet<VersionStatement>(ownLineDecided ? statements.Where(s => s.Reason == empty) : [], ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// The scheme to compare in: a package's ecosystem; otherwise the record's own version type when it
    /// names a specific one, then the component's, then the numeric fallback: the first that reads the
    /// installed version and every bound of the statement, else the first that reads the installed version.
    /// A record often names its type on some entries only: Mattermost tags «0 to 6.3.2» semver and leaves
    /// the unaffected «6.3.1.0» untyped, which semver cannot read; the numeric fallback reads both.
    /// </summary>
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

    /// <summary><c>InAffectedRange</c> → <c>in_affected_range</c>.</summary>
    internal static string Snake(string name) =>
        string.Concat(name.Select((c, i) => char.IsUpper(c) ? (i > 0 ? "_" : string.Empty) + char.ToLowerInvariant(c) : c.ToString()));
}
