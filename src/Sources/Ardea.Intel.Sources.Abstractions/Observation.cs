namespace Ardea.Intel.Sources.Abstractions;

/// <summary>
/// The result of <see cref="ISourceProvider.Normalize"/>: an observation, or the reason why the raw
/// record goes to quarantine (master plan 6.1). A quarantined record blocks nothing and is kept.
/// </summary>
public sealed record NormalizationResult
{
    private NormalizationResult(Observation? observation, string? quarantineReason)
    {
        Observation = observation;
        QuarantineReason = quarantineReason;
    }

    public Observation? Observation { get; }

    public string? QuarantineReason { get; }

    public static NormalizationResult Success(Observation observation) => new(observation, null);

    public static NormalizationResult Quarantine(string reason) => new(null, reason);
}

/// <summary>What a record is about (master plan 4.3, 5.1).</summary>
public enum ObservationKind
{
    /// <summary>A record about one vulnerability, e.g. a CVE record.</summary>
    Vulnerability,

    /// <summary>A document that is evidence for zero to many vulnerabilities, e.g. a vendor advisory.</summary>
    Advisory,

    /// <summary>A warning without a vulnerability identifier, e.g. a phishing warning; bound to its source.</summary>
    Warning,
}

/// <summary>
/// The normalised statements of one source about one raw record (master plan 6.1). It contains
/// only what the source says; missing information stays empty and is never guessed.
/// </summary>
public sealed record Observation
{
    public required ObservationKind Kind { get; init; }

    public string? Title { get; init; }

    public string? Summary { get; init; }

    /// <summary>Full text, already reduced to a safe subset if it was HTML.</summary>
    public string? Description { get; init; }

    /// <summary>BCP 47 language tag of the texts, e.g. <c>en</c>.</summary>
    public string? Language { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public DateTimeOffset? ModifiedAt { get; init; }

    /// <summary>One block per vulnerability the record speaks about; empty for a warning without identifier.</summary>
    public IReadOnlyList<ClaimBlock> Claims { get; init; } = [];

    /// <summary>Links of the document itself (for advisories and warnings).</summary>
    public IReadOnlyList<Reference> References { get; init; } = [];
}

/// <summary>An identifier with its scheme, e.g. (<c>CVE</c>, <c>CVE-2026-1234</c>), (<c>GHSA</c>, …).</summary>
public sealed record Identifier(string Scheme, string Value);

public enum VulnerabilityState
{
    Published,
    Rejected,
    Disputed,
}

/// <summary>Everything a source says about one vulnerability.</summary>
public sealed record ClaimBlock
{
    /// <summary>Identifiers of the same vulnerability (CVE, GHSA, OSV, EUVD); a shared alias joins entries.</summary>
    public required IReadOnlyList<Identifier> Aliases { get; init; }

    /// <summary>Documents that mention it (RHSA, USN, vendor advisory); they join nothing.</summary>
    public IReadOnlyList<Identifier> Related { get; init; } = [];

    public VulnerabilityState State { get; init; } = VulnerabilityState.Published;

    /// <summary>Title specific to this vulnerability, when the record has one per block.</summary>
    public string? Title { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<SeverityMetric> Metrics { get; init; } = [];

    public IReadOnlyList<AffectedProduct> Affected { get; init; } = [];

    public Exploitation? Exploitation { get; init; }

    public Remediation? Remediation { get; init; }

    public IReadOnlyList<Reference> References { get; init; } = [];

    /// <summary>CWE identifiers, e.g. <c>CWE-79</c>.</summary>
    public IReadOnlyList<string> Weaknesses { get; init; } = [];

    public DateTimeOffset? PublishedAt { get; init; }

    public DateTimeOffset? ModifiedAt { get; init; }
}

/// <summary>A severity statement: system and version, vector, score, and who assigned it.</summary>
public sealed record SeverityMetric(string System, string Version, string? Vector, double? Score)
{
    /// <summary>Who assigned it: the CNA, a CISA-ADP enrichment, NVD, …</summary>
    public string? Assigner { get; init; }
}

public enum AffectedStatus
{
    Affected,
    Unaffected,
    Unknown,
}

/// <summary>A product the vulnerability affects, as the source names it (master plan 4.4 <c>entry_affected</c>).</summary>
public sealed record AffectedProduct
{
    public string? Vendor { get; init; }

    public string? Product { get; init; }

    /// <summary>CPE 2.3 strings as given.</summary>
    public IReadOnlyList<string> Cpes { get; init; } = [];

    /// <summary>Package URL, e.g. <c>pkg:npm/express</c> (without version).</summary>
    public string? Purl { get; init; }

    public IReadOnlyList<string> Platforms { get; init; } = [];

    /// <summary>The version scheme the source names, e.g. <c>semver</c>, <c>rpm</c>, <c>custom</c>; null when absent.</summary>
    public string? VersionScheme { get; init; }

    public IReadOnlyList<VersionRange> Versions { get; init; } = [];

    /// <summary>The record's status for every version not named; null means not given ("unknown" per the format).</summary>
    public AffectedStatus? DefaultStatus { get; init; }
}

/// <summary>
/// A version statement as in the record: an exact version or a range with lower bound and upper
/// bound (fixed: exclusive, last affected: inclusive). <see cref="Raw"/> keeps the original text.
/// </summary>
public sealed record VersionRange
{
    public AffectedStatus Status { get; init; } = AffectedStatus.Affected;

    public string? Exact { get; init; }

    public string? Introduced { get; init; }

    public string? Fixed { get; init; }

    public string? LastAffected { get; init; }

    /// <summary>The source's own version scheme for this range, when it differs per range.</summary>
    public string? Scheme { get; init; }

    public string? Raw { get; init; }
}

public enum ExploitationState
{
    None,
    ProofOfConcept,
    Active,
}

public sealed record Exploitation(ExploitationState State)
{
    public string? Evidence { get; init; }

    public DateTimeOffset? Since { get; init; }

    /// <summary>SSVC decision points as given, e.g. <c>Exploitation: active</c>.</summary>
    public IReadOnlyDictionary<string, string> Ssvc { get; init; } = new Dictionary<string, string>();
}

public sealed record Remediation
{
    public bool FixAvailable { get; init; }

    public IReadOnlyList<string> Solutions { get; init; } = [];

    public IReadOnlyList<string> Workarounds { get; init; } = [];
}

/// <summary>A link with tags such as <c>patch</c>, <c>exploit</c>, <c>vendor-advisory</c>.</summary>
public sealed record Reference(Uri Url)
{
    public IReadOnlyList<string> Tags { get; init; } = [];
}
