using System.Text.Json;

namespace Ardea.Intel.Persistence;

/// <summary>The consolidated entry: computed from all observations, never edited (master plan 4.3, 4.4).</summary>
internal sealed class EntryRow
{
    public Guid Id { get; set; }

    /// <summary>vulnerability or warning.</summary>
    public required string Kind { get; set; }

    /// <summary>The best-ranked identifier, e.g. <c>CVE-2026-1234</c>; for a warning its source key and document id.</summary>
    public required string PrimaryKey { get; set; }

    public string? Title { get; set; }

    public string? Summary { get; set; }

    public string? Description { get; set; }

    /// <summary>published, rejected, disputed or merged.</summary>
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

    /// <summary>none, poc or active.</summary>
    public required string Exploitation { get; set; }

    public JsonDocument? ExploitationEvidence { get; set; }

    /// <summary>EPSS class from the signals (task 1.8): <c>high</c> above 50 %, else <c>low</c>; null without a value.</summary>
    public string? EpssClass { get; set; }

    public bool FixAvailable { get; set; }

    public string[] CweIds { get; set; } = [];

    /// <summary>The versions the description and solutions name as fixes, for the version check (master plan 6.6).</summary>
    public string[] FixVersionsInText { get; set; } = [];

    /// <summary>An advisory or warning of a national authority names this entry (master plan 5.6, 7.6).</summary>
    public bool AuthorityWarning { get; set; }

    /// <summary>Which source each field comes from.</summary>
    public required JsonDocument Provenance { get; set; }

    /// <summary>All vectors, solutions and workarounds as received, for display.</summary>
    public JsonDocument? Details { get; set; }
}

internal sealed class EntryAliasRow
{
    public Guid Id { get; set; }

    public Guid EntryId { get; set; }

    public required string Scheme { get; set; }

    public required string Value { get; set; }
}

/// <summary>An affected product per source (master plan 4.4 <c>entry_affected</c>).</summary>
internal sealed class EntryAffectedRow
{
    public Guid Id { get; set; }

    public Guid EntryId { get; set; }

    public Guid SourceId { get; set; }

    public string? Vendor { get; set; }

    public string? Product { get; set; }

    public string? ProductKey { get; set; }

    public string? CpeVendor { get; set; }

    public string? CpeProduct { get; set; }

    public string? PurlType { get; set; }

    public string? PurlName { get; set; }

    public string? VersionScheme { get; set; }

    public string? Introduced { get; set; }

    public string? Fixed { get; set; }

    public string? LastAffected { get; set; }

    public string? Exact { get; set; }

    /// <summary>affected, unaffected or unknown.</summary>
    public required string Status { get; set; }

    /// <summary>The record's status for unnamed versions; <c>unknown</c> when the record says nothing.</summary>
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

/// <summary>Which sources and records back an entry, and how (master plan 4.4).</summary>
internal sealed class EntrySourceRow
{
    public Guid Id { get; set; }

    public Guid EntryId { get; set; }

    public Guid SourceId { get; set; }

    public Guid? SourceRecordId { get; set; }

    /// <summary>describes, mentions or signal.</summary>
    public required string Relation { get; set; }

    public string? Url { get; set; }
}

/// <summary>Change history of an entry (master plan 4.4, 6.4).</summary>
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

/// <summary>
/// Signals per identifier, kept apart from the entry (master plan 4.4): KEV listing and EPSS. An entry
/// is touched only when the EPSS class or the KEV listing changes.
/// </summary>
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

    /// <summary><c>high</c> above 50 % (with a five-point band against flapping) or <c>low</c>.</summary>
    public string? EpssClass { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A case for the instance administrator's review list (master plan 6.2 rule 5, 6.9): an identifier
/// that would join entries which each carry their own CVE id. Such entries are never merged
/// automatically; the case stays here until the administrator decides it.
/// </summary>
internal sealed class ReviewItemRow
{
    public Guid Id { get; set; }

    /// <summary><c>cve_conflict</c>.</summary>
    public required string Kind { get; set; }

    /// <summary>What the case is about, unique per kind: the CVE ids, sorted and comma-separated.</summary>
    public required string SubjectKey { get; set; }

    public Guid[] EntryIds { get; set; } = [];

    /// <summary>The identifiers that connect the entries, e.g. a GHSA id listing both CVE ids.</summary>
    public string[] SharedAliases { get; set; } = [];

    /// <summary><c>open</c> or <c>kept_separate</c>; a decided case is not reopened by the same evidence.</summary>
    public required string State { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public Guid? DecidedBy { get; set; }
}
