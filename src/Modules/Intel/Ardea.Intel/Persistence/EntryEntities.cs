using System.Text.Json;

namespace Ardea.Intel.Persistence;

internal sealed class EntryRow
{
    public Guid Id { get; set; }

    public required string Kind { get; set; }

    public required string PrimaryKey { get; set; }

    public string? Title { get; set; }

    public string? Summary { get; set; }

    public string? Description { get; set; }

    public required string State { get; set; }

    public Guid? MergedIntoId { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public int Revision { get; set; }

    public required string MaterialFingerprint { get; set; }

    public required string RemediationFingerprint { get; set; }

    public double? CvssScore { get; set; }

    public string? CvssVersion { get; set; }

    public string? CvssVector { get; set; }

    public required string Exploitation { get; set; }

    public JsonDocument? ExploitationEvidence { get; set; }

    public string? EpssClass { get; set; }

    public bool FixAvailable { get; set; }

    public string[] CweIds { get; set; } = [];

    public string[] FixVersionsInText { get; set; } = [];

    public bool AuthorityWarning { get; set; }

    public required JsonDocument Provenance { get; set; }

    public JsonDocument? Details { get; set; }
}

internal sealed class EntryAliasRow
{
    public Guid Id { get; set; }

    public Guid EntryId { get; set; }

    public required string Scheme { get; set; }

    public required string Value { get; set; }
}

internal sealed class EntryAffectedRow
{
    public Guid Id { get; set; }

    public Guid EntryId { get; set; }

    public Guid SourceId { get; set; }

    public string? Vendor { get; set; }

    public string? Product { get; set; }

    public string? ProductKey { get; set; }

    public string? LearnedFrom { get; set; }

    public string? CpeVendor { get; set; }

    public string? CpeProduct { get; set; }

    public string? PurlType { get; set; }

    public string? PurlName { get; set; }

    public string? VersionScheme { get; set; }

    public string? Introduced { get; set; }

    public string? Fixed { get; set; }

    public string? LastAffected { get; set; }

    public string? Exact { get; set; }

    public required string Status { get; set; }

    public required string DefaultStatus { get; set; }
}

internal sealed class EntryReferenceRow
{
    public Guid Id { get; set; }

    public Guid EntryId { get; set; }

    public required string Url { get; set; }

    public string[] Tags { get; set; } = [];

    public Guid SourceId { get; set; }
}

internal sealed class EntrySourceRow
{
    public Guid Id { get; set; }

    public Guid EntryId { get; set; }

    public Guid SourceId { get; set; }

    public Guid? SourceRecordId { get; set; }

    public required string Relation { get; set; }

    public string? Url { get; set; }
}

internal sealed class EntryChangeRow
{
    public Guid Id { get; set; }

    public Guid EntryId { get; set; }

    public int Revision { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public string[] ChangeKinds { get; set; } = [];

    public bool IsMaterial { get; set; }

    public JsonDocument? Diff { get; set; }

    public string? TriggeredBy { get; set; }
}

internal sealed class EntrySignalRow
{
    public Guid Id { get; set; }

    public required string Scheme { get; set; }

    public required string Value { get; set; }

    public DateOnly? KevListedOn { get; set; }

    public string? KevUrl { get; set; }

    public bool? KevKnownRansomware { get; set; }

    public double? EpssScore { get; set; }

    public double? EpssPercentile { get; set; }

    public string? EpssClass { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class ReviewItemRow
{
    public Guid Id { get; set; }

    public required string Kind { get; set; }

    public required string SubjectKey { get; set; }

    public Guid[] EntryIds { get; set; } = [];

    public string[] SharedAliases { get; set; } = [];

    public required string State { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public Guid? DecidedBy { get; set; }
}
