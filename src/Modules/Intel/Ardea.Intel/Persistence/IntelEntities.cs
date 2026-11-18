using System.Text.Json;

namespace Ardea.Intel.Persistence;

/// <summary>Health of a source (master plan 5.4, 6.8).</summary>
internal static class SourceHealth
{
    public const string Healthy = "healthy";
    public const string Degraded = "degraded";
    public const string Failing = "failing";
    public const string Disabled = "disabled";
}

/// <summary>A source: master data from its descriptor, operator settings, and sync state (master plan 4.4).</summary>
internal sealed class SourceRow
{
    public Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Name { get; set; }

    /// <summary>record, advisory or signal.</summary>
    public required string Kind { get; set; }

    public required string Operator { get; set; }

    /// <summary>authority, mandated, recognized, vendor or community (master plan 5.6).</summary>
    public required string OperatorKind { get; set; }

    /// <summary>The adapter's type name; the source's code lives in the image.</summary>
    public required string Adapter { get; set; }

    public bool Enabled { get; set; }

    public TimeSpan Interval { get; set; }

    public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");

    public required string Attribution { get; set; }

    /// <summary>full or metadata_only.</summary>
    public required string TermsMode { get; set; }

    public int ParserVersion { get; set; }

    public DateTimeOffset NextSyncAt { get; set; }

    /// <summary>The cursor of the last completed checkpoint; opaque to the core.</summary>
    public string? Cursor { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public int ConsecutiveFailures { get; set; }

    public required string Health { get; set; }

    public string? HealthReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One run of a source (master plan 4.4 <c>source_run</c>).</summary>
internal sealed class SourceRunRow
{
    public Guid Id { get; set; }

    public Guid SourceId { get; set; }

    /// <summary>incremental, backfill or single.</summary>
    public required string Mode { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>running, succeeded or failed.</summary>
    public required string Outcome { get; set; }

    public int Fetched { get; set; }

    public int Changed { get; set; }

    public int Quarantined { get; set; }

    public int Checkpoints { get; set; }

    public string? Error { get; set; }
}

/// <summary>The current raw record per external id; revisions keep earlier states (master plan 4.4).</summary>
internal sealed class SourceRecordRow
{
    public Guid Id { get; set; }

    public Guid SourceId { get; set; }

    public required string ExternalId { get; set; }

    public string? Url { get; set; }

    public required string ContentType { get; set; }

    /// <summary>SHA-256 of the canonical content.</summary>
    public required string ContentHash { get; set; }

    public required string Payload { get; set; }

    public int Revision { get; set; }

    public DateTimeOffset? SourcePublishedAt { get; set; }

    public DateTimeOffset? SourceModifiedAt { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>pending, parsed or quarantined.</summary>
    public required string ParseStatus { get; set; }

    /// <summary>Why the record could not be translated (master plan 6.1); shown on the sources page.</summary>
    public string? QuarantineReason { get; set; }

    /// <summary>The adapter's parser version of the last normalisation.</summary>
    public int? ParserVersion { get; set; }
}

/// <summary>
/// The normalised statements of the current raw record: exactly one per record, overwritten by
/// every new revision (master plan 4.3, 4.4).
/// </summary>
internal sealed class ObservationRow
{
    public Guid Id { get; set; }

    public Guid SourceRecordId { get; set; }

    public Guid SourceId { get; set; }

    public int RecordRevision { get; set; }

    /// <summary>vulnerability, advisory or warning.</summary>
    public required string Kind { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Language { get; set; }

    /// <summary>The claim blocks as JSON: aliases, related documents, metrics, affected products, …</summary>
    public required JsonDocument Claims { get; set; }

    /// <summary>Every alias of every claim block, for identity resolution (master plan 6.2).</summary>
    public string[] Aliases { get; set; } = [];

    /// <summary>parsed, or extracted by a language model (not in the MVP).</summary>
    public required string Origin { get; set; }

    public int ParserVersion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class SourceRecordRevisionRow
{
    public Guid Id { get; set; }

    public Guid SourceRecordId { get; set; }

    public int Revision { get; set; }

    public required string ContentHash { get; set; }

    public required string Payload { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}
