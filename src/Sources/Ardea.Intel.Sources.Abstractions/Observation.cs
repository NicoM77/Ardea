namespace Ardea.Intel.Sources.Abstractions;

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

public enum ObservationKind
{
    Vulnerability,

    Advisory,

    Warning,
}

public sealed record Observation
{
    public required ObservationKind Kind { get; init; }

    public string? Title { get; init; }

    public string? Summary { get; init; }

    public string? Description { get; init; }

    public string? Language { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public DateTimeOffset? ModifiedAt { get; init; }

    public IReadOnlyList<ClaimBlock> Claims { get; init; } = [];

    public IReadOnlyList<Reference> References { get; init; } = [];
}

public sealed record Identifier(string Scheme, string Value);

public enum VulnerabilityState
{
    Published,
    Rejected,
    Disputed,
}

public sealed record ClaimBlock
{
    public required IReadOnlyList<Identifier> Aliases { get; init; }

    public IReadOnlyList<Identifier> Related { get; init; } = [];

    public VulnerabilityState State { get; init; } = VulnerabilityState.Published;

    public string? Title { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<SeverityMetric> Metrics { get; init; } = [];

    public IReadOnlyList<AffectedProduct> Affected { get; init; } = [];

    public Exploitation? Exploitation { get; init; }

    public Remediation? Remediation { get; init; }

    public IReadOnlyList<Reference> References { get; init; } = [];

    public IReadOnlyList<string> Weaknesses { get; init; } = [];

    public DateTimeOffset? PublishedAt { get; init; }

    public DateTimeOffset? ModifiedAt { get; init; }
}

public sealed record SeverityMetric(string System, string Version, string? Vector, double? Score)
{
    public string? Assigner { get; init; }
}

public enum AffectedStatus
{
    Affected,
    Unaffected,
    Unknown,
}

public sealed record AffectedProduct
{
    public string? Vendor { get; init; }

    public string? Product { get; init; }

    public IReadOnlyList<string> Cpes { get; init; } = [];

    public string? Purl { get; init; }

    public IReadOnlyList<string> Platforms { get; init; } = [];

    public string? VersionScheme { get; init; }

    public IReadOnlyList<VersionRange> Versions { get; init; } = [];

    public AffectedStatus? DefaultStatus { get; init; }
}

public sealed record VersionRange
{
    public AffectedStatus Status { get; init; } = AffectedStatus.Affected;

    public string? Exact { get; init; }

    public string? Introduced { get; init; }

    public string? Fixed { get; init; }

    public string? LastAffected { get; init; }

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

    public IReadOnlyDictionary<string, string> Ssvc { get; init; } = new Dictionary<string, string>();
}

public sealed record Remediation
{
    public bool FixAvailable { get; init; }

    public IReadOnlyList<string> Solutions { get; init; } = [];

    public IReadOnlyList<string> Workarounds { get; init; } = [];
}

public sealed record Reference(Uri Url)
{
    public IReadOnlyList<string> Tags { get; init; } = [];
}
