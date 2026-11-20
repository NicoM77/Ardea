using System.Text.Json;

namespace Ardea.Intel.Persistence;

internal static class SourceHealth
{
    public const string Healthy = "healthy";
    public const string Degraded = "degraded";
    public const string Failing = "failing";
    public const string Disabled = "disabled";
}

internal sealed class SourceRow
{
    public Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Name { get; set; }

    public required string Kind { get; set; }

    public required string Operator { get; set; }

    public required string OperatorKind { get; set; }

    public required string Adapter { get; set; }

    public bool Enabled { get; set; }

    public TimeSpan Interval { get; set; }

    public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");

    public required string Attribution { get; set; }

    public required string TermsMode { get; set; }

    public int ParserVersion { get; set; }

    public DateTimeOffset NextSyncAt { get; set; }

    public string? Cursor { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public int ConsecutiveFailures { get; set; }

    public required string Health { get; set; }

    public string? HealthReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class SourceRunRow
{
    public Guid Id { get; set; }

    public Guid SourceId { get; set; }

    public required string Mode { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public required string Outcome { get; set; }

    public int Fetched { get; set; }

    public int Changed { get; set; }

    public int Quarantined { get; set; }

    public int Checkpoints { get; set; }

    public string? Error { get; set; }
}

internal sealed class SourceRecordRow
{
    public Guid Id { get; set; }

    public Guid SourceId { get; set; }

    public required string ExternalId { get; set; }

    public string? Url { get; set; }

    public required string ContentType { get; set; }

    public required string ContentHash { get; set; }

    public required string Payload { get; set; }

    public int Revision { get; set; }

    public DateTimeOffset? SourcePublishedAt { get; set; }

    public DateTimeOffset? SourceModifiedAt { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public required string ParseStatus { get; set; }

    public string? QuarantineReason { get; set; }

    public int? ParserVersion { get; set; }
}

internal sealed class ObservationRow
{
    public Guid Id { get; set; }

    public Guid SourceRecordId { get; set; }

    public Guid SourceId { get; set; }

    public int RecordRevision { get; set; }

    public required string Kind { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Language { get; set; }

    public required JsonDocument Claims { get; set; }

    public string[] Aliases { get; set; } = [];

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
