using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Correlation;

/// <summary>What one observation contributes to one entry: the claim blocks that share its aliases.</summary>
internal sealed record Contribution(
    Guid SourceId,
    string SourceKey,
    string OperatorKind,
    Guid SourceRecordId,
    string? RecordUrl,
    Observation Observation,
    IReadOnlyList<ClaimBlock> Claims);

internal sealed record AffectedData(
    Guid SourceId, string? Vendor, string? Product, string? ProductKey, string? CpeVendor, string? CpeProduct,
    string? PurlType, string? PurlName, string? VersionScheme, string? Introduced, string? Fixed, string? LastAffected,
    string? Exact, string Status, string DefaultStatus);

internal sealed record SourceLink(Guid SourceId, Guid? SourceRecordId, string Relation, string? Url);

internal sealed record ExploitationEvidence(string Source, string State, string? Evidence, DateTimeOffset? Since);

/// <summary>The computed entry (master plan 6.3): every field with the source it came from.</summary>
internal sealed record EntryProjection
{
    public required string Kind { get; init; }

    public required string PrimaryKey { get; init; }

    public required IReadOnlyList<Identifier> Aliases { get; init; }

    public string? Title { get; init; }

    public string? Summary { get; init; }

    public string? Description { get; init; }

    public required string State { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public double? CvssScore { get; init; }

    public string? CvssVersion { get; init; }

    public string? CvssVector { get; init; }

    public required string Exploitation { get; init; }

    public IReadOnlyList<ExploitationEvidence> ExploitationEvidence { get; init; } = [];

    public bool FixAvailable { get; init; }

    public IReadOnlyList<string> CweIds { get; init; } = [];

    public bool AuthorityWarning { get; init; }

    public required IReadOnlyDictionary<string, string> Provenance { get; init; }

    public IReadOnlyList<AffectedData> Affected { get; init; } = [];

    public IReadOnlyList<(string Url, string[] Tags, Guid SourceId)> References { get; init; } = [];

    public IReadOnlyList<SourceLink> Sources { get; init; } = [];

    public IReadOnlyList<SeverityMetric> AllMetrics { get; init; } = [];

    public IReadOnlyList<string> Solutions { get; init; } = [];

    public IReadOnlyList<string> Workarounds { get; init; } = [];

    /// <summary>From the signals (task 1.8): <c>high</c>, <c>low</c> or none.</summary>
    public string? EpssClass { get; init; }

    /// <summary>Hash over the fields that can change an assessment (master plan 6.4 step 4).</summary>
    public string MaterialFingerprint(string? epssClass) => Hash(
        State,
        SeverityClass(CvssScore),
        CvssVector ?? string.Empty,
        string.Join('\n', Affected.Select(a => string.Join('|', a.ProductKey, a.CpeVendor, a.CpeProduct, a.PurlType, a.PurlName, a.VersionScheme, a.Introduced, a.Fixed, a.LastAffected, a.Exact, a.Status, a.DefaultStatus)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
        Exploitation,
        FixAvailable.ToString(),
        AuthorityWarning.ToString(),
        epssClass ?? string.Empty);

    /// <summary>Hash over what tells people what to do: a new workaround updates the answer without reassessment.</summary>
    public string RemediationFingerprint() => Hash(
        string.Join('\n', Solutions.Order(StringComparer.Ordinal)),
        string.Join('\n', Workarounds.Order(StringComparer.Ordinal)),
        string.Join('\n', Affected.Select(a => a.Fixed).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));

    public static string SeverityClass(double? score) => score switch
    {
        null => "unrated",
        >= 9.0 => "critical",
        >= 7.0 => "high",
        >= 4.0 => "medium",
        > 0 => "low",
        _ => "none",
    };

    private static string Hash(params string[] parts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts))));
}

/// <summary>
/// Computes an entry from all observations that share its aliases, with the fixed precedence rules
/// of master plan 6.3. Deterministic: the same observations give the same entry.
/// </summary>
internal static class Projection
{
    /// <summary>Fixed ranking of sources in the MVP (master plan 6.3): the identifier's issuer first.</summary>
    private static readonly Dictionary<string, int> SourceRank = new(StringComparer.Ordinal)
    {
        ["cve-program"] = 0,
        ["osv"] = 1,
    };

    private static readonly string[] SchemeRank = ["CVE", "GHSA", "OSV", "EUVD"];

    private static readonly string[] AdpAssigners = ["CISA-ADP", "CISA", "NVD", "nvd@nist.gov", "CVE"];

    public static EntryProjection Compute(IReadOnlyList<Contribution> contributions, string? warningKey = null)
    {
        var ordered = contributions.OrderBy(Rank).ThenBy(c => c.SourceKey, StringComparer.Ordinal).ThenBy(c => c.SourceRecordId).ToList();
        var provenance = new Dictionary<string, string>(StringComparer.Ordinal);

        if (warningKey is not null)
        {
            // A warning without identifier stays bound to its one source (master plan 6.2 rule 6).
            var warning = ordered[0];
            provenance["title"] = warning.SourceKey;
            return new EntryProjection
            {
                Kind = "warning",
                PrimaryKey = warningKey,
                Aliases = [],
                Title = warning.Observation.Title,
                Summary = warning.Observation.Summary,
                Description = warning.Observation.Description,
                State = "published",
                PublishedAt = warning.Observation.PublishedAt,
                Exploitation = "none",
                AuthorityWarning = IsAuthority(warning.OperatorKind),
                Provenance = provenance,
                References = [.. warning.Observation.References.Select(r => (r.Url.AbsoluteUri, r.Tags.ToArray(), warning.SourceId))],
                Sources = [new SourceLink(warning.SourceId, warning.SourceRecordId, "describes", warning.RecordUrl)],
            };
        }

        var aliases = ordered.SelectMany(c => c.Claims).SelectMany(b => b.Aliases)
            .DistinctBy(a => (a.Scheme.ToUpperInvariant(), a.Value), EqualityComparer<(string, string)>.Default)
            .OrderBy(a => SchemeIndex(a.Scheme)).ThenBy(a => a.Value, StringComparer.Ordinal)
            .ToList();
        var primary = aliases.FirstOrDefault() ?? throw new InvalidOperationException("An entry needs at least one alias.");

        // Text: records that describe the vulnerability before documents that mention it.
        var describing = ordered.Where(c => c.Observation.Kind == ObservationKind.Vulnerability).ToList();
        var mentioning = ordered.Where(c => c.Observation.Kind != ObservationKind.Vulnerability).ToList();
        var textSources = describing.Concat(mentioning).ToList();
        var title = First(textSources, c => c.Claims.Select(b => b.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? (c.Observation.Kind == ObservationKind.Vulnerability ? c.Observation.Title : null), "title", provenance);
        var description = First(textSources, c => c.Claims.Select(b => b.Description).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? (c.Observation.Kind == ObservationKind.Vulnerability ? c.Observation.Description : null), "description", provenance);
        var summary = First(mentioning, c => c.Observation.Summary, "summary", provenance);

        // The issuer withdrawing overrules everything else.
        var issuerStates = describing.Where(c => Rank(c) == 0).SelectMany(c => c.Claims).Select(b => b.State).ToList();
        var allStates = ordered.SelectMany(c => c.Claims).Select(b => b.State).ToList();
        var state = issuerStates.Contains(VulnerabilityState.Rejected) ? "rejected"
            : (issuerStates.Count > 0 ? issuerStates : allStates).Contains(VulnerabilityState.Disputed) ? "disputed"
            : "published";

        var metrics = ordered.SelectMany(c => c.Claims.SelectMany(b => b.Metrics).Select(m => (Contribution: c, Metric: m))).ToList();
        var severity = metrics
            .Where(m => m.Metric.Score is not null)
            .OrderBy(m => MetricGroup(m.Contribution, m.Metric))
            .ThenByDescending(m => double.TryParse(m.Metric.Version, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .Select(m => ((SeverityMetric, string)?)(m.Metric, m.Contribution.SourceKey))
            .FirstOrDefault();
        if (severity is { } chosen)
        {
            provenance["severity"] = chosen.Item1.Assigner is { } assigner ? $"{chosen.Item2}/{assigner}" : chosen.Item2;
        }

        var evidence = ordered
            .SelectMany(c => c.Claims.Where(b => b.Exploitation is not null).Select(b => new ExploitationEvidence(c.SourceKey, State(b.Exploitation!.State), b.Exploitation.Evidence, b.Exploitation.Since)))
            .DistinctBy(e => (e.Source, e.State, e.Evidence))
            .ToList();
        var exploitation = evidence.Select(e => e.State).OrderByDescending(StateOrder).FirstOrDefault() ?? "none";

        var affected = ordered.SelectMany(c => c.Claims.SelectMany(b => b.Affected).SelectMany(a => Rows(c.SourceId, a))).Distinct().ToList();
        var solutions = ordered.SelectMany(c => c.Claims).SelectMany(b => b.Remediation?.Solutions ?? []).Distinct(StringComparer.Ordinal).ToList();
        var workarounds = ordered.SelectMany(c => c.Claims).SelectMany(b => b.Remediation?.Workarounds ?? []).Distinct(StringComparer.Ordinal).ToList();
        var references = ordered
            .SelectMany(c => c.Claims.SelectMany(b => b.References).Concat(c.Observation.References).Select(r => (Url: r.Url.AbsoluteUri, r.Tags, c.SourceId)))
            .GroupBy(r => r.Url, StringComparer.Ordinal)
            .Select(g => (g.Key, g.SelectMany(r => r.Tags).Distinct(StringComparer.Ordinal).ToArray(), g.First().SourceId))
            .ToList();
        var fix = ordered.SelectMany(c => c.Claims).Any(b => b.Remediation?.FixAvailable == true)
            || references.Any(r => r.Item2.Contains("patch", StringComparer.OrdinalIgnoreCase));

        var authority = mentioning.Any(c => IsAuthority(c.OperatorKind));
        if (authority)
        {
            provenance["authorityWarning"] = mentioning.First(c => IsAuthority(c.OperatorKind)).SourceKey;
        }

        title ??= DerivedTitle(affected, ordered.SelectMany(c => c.Claims).SelectMany(b => b.Weaknesses).FirstOrDefault(), provenance);

        return new EntryProjection
        {
            Kind = "vulnerability",
            PrimaryKey = primary.Value,
            Aliases = aliases,
            Title = title,
            Summary = summary,
            Description = description,
            State = state,
            PublishedAt = ordered.SelectMany(c => c.Claims).Select(b => b.PublishedAt).Concat(describing.Select(c => c.Observation.PublishedAt)).Where(d => d is not null).Min(),
            CvssScore = severity?.Item1.Score,
            CvssVersion = severity?.Item1.Version,
            CvssVector = severity?.Item1.Vector,
            Exploitation = exploitation,
            ExploitationEvidence = evidence,
            FixAvailable = fix,
            CweIds = [.. ordered.SelectMany(c => c.Claims).SelectMany(b => b.Weaknesses).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            AuthorityWarning = authority,
            Provenance = provenance,
            Affected = affected,
            References = references,
            Sources = [.. ordered.Select(c => new SourceLink(c.SourceId, c.SourceRecordId, c.Observation.Kind == ObservationKind.Vulnerability ? "describes" : "mentions", c.RecordUrl)).Distinct()],
            AllMetrics = [.. metrics.Select(m => m.Metric)],
            Solutions = solutions,
            Workarounds = workarounds,
        };
    }

    /// <summary>A title when no source gives one (25 % of records): built from product and weakness, never invented.</summary>
    private static string? DerivedTitle(IReadOnlyList<AffectedData> affected, string? weakness, Dictionary<string, string> provenance)
    {
        var product = affected.Select(a => string.Join(' ', new[] { a.Vendor, a.Product }.Where(p => !string.IsNullOrWhiteSpace(p)))).FirstOrDefault(p => p.Length > 0);
        if (product is null)
        {
            return null;
        }

        provenance["title"] = "derived";
        return weakness is null ? $"Vulnerability in {product}" : $"{weakness} in {product}";
    }

    private static IEnumerable<AffectedData> Rows(Guid sourceId, AffectedProduct product)
    {
        // CPE pair and package in the catalogue's canonical forms, so the candidate search compares like with like.
        var cpe = product.Cpes.Select(c => Cpe.TryParse(c, out var parsed) ? parsed : null).FirstOrDefault(c => c is not null && c.Vendor is not (Cpe.Any or Cpe.NotApplicable));
        var package = PackageCoordinate.TryParsePurl(product.Purl, out var coordinate, out _) ? coordinate : null;
        // The raw key; the correlation replaces it with the catalogue's key where the catalogue knows the product.
        var key = ProductKey.TryCreate(product.Vendor, product.Product ?? package?.Name, out var raw) ? raw.Value : null;
        var defaultStatus = Status(product.DefaultStatus) ?? "unknown";
        if (product.Versions.Count == 0)
        {
            // No versions named: the default status says it all; without it, unknown.
            yield return new AffectedData(sourceId, product.Vendor, product.Product, key, cpe?.Vendor, cpe?.Product, package?.Type, package?.Name,
                product.VersionScheme, null, null, null, null, Status(product.DefaultStatus) ?? "unknown", defaultStatus);
            yield break;
        }

        foreach (var range in product.Versions)
        {
            yield return new AffectedData(sourceId, product.Vendor, product.Product, key, cpe?.Vendor, cpe?.Product, package?.Type, package?.Name,
                range.Scheme ?? product.VersionScheme, range.Introduced, range.Fixed, range.LastAffected, range.Exact,
                Status(range.Status) ?? "unknown", defaultStatus);
        }
    }

    private static int Rank(Contribution c) =>
        SourceRank.TryGetValue(c.SourceKey, out var rank) ? rank
        : c.OperatorKind switch
        {
            "authority" => 2,
            "mandated" => 3,
            "vendor" => 4,
            "recognized" => 5,
            _ => 6,
        };

    /// <summary>The issuer's CNA first, then CISA's enrichment, then NVD, then the rest by source rank (master plan 6.3).</summary>
    private static int MetricGroup(Contribution c, SeverityMetric m)
    {
        var adp = m.Assigner is { } a && AdpAssigners.Contains(a, StringComparer.OrdinalIgnoreCase);
        if (Rank(c) == 0 && !adp)
        {
            return 0;
        }

        if (m.Assigner is "CISA-ADP" or "CISA")
        {
            return 1;
        }

        return m.Assigner is "NVD" or "nvd@nist.gov" ? 2 : 3 + Rank(c);
    }

    private static int SchemeIndex(string scheme)
    {
        var index = Array.IndexOf(SchemeRank, scheme.ToUpperInvariant());
        return index < 0 ? SchemeRank.Length : index;
    }

    private static bool IsAuthority(string operatorKind) => operatorKind is "authority" or "mandated";

    private static string? First(IEnumerable<Contribution> sources, Func<Contribution, string?> pick, string field, Dictionary<string, string> provenance)
    {
        foreach (var source in sources)
        {
            if (pick(source) is { Length: > 0 } value)
            {
                provenance[field] = source.SourceKey;
                return value;
            }
        }

        return null;
    }

    private static string State(ExploitationState state) => state switch
    {
        ExploitationState.Active => "active",
        ExploitationState.ProofOfConcept => "poc",
        _ => "none",
    };

    private static int StateOrder(string state) => state switch
    {
        "active" => 2,
        "poc" => 1,
        _ => 0,
    };

    private static string? Status(AffectedStatus? status) => status switch
    {
        AffectedStatus.Affected => "affected",
        AffectedStatus.Unaffected => "unaffected",
        AffectedStatus.Unknown => "unknown",
        _ => null,
    };
}
