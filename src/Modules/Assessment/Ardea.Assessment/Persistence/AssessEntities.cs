using System.Text.Json;
using Ardea.Platform.Persistence;

namespace Ardea.Assessment.Persistence;

internal sealed class Alert : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid EntryId { get; set; }

    public required string EntryKey { get; set; }

    public int? Number { get; set; }

    public DateTimeOffset? FirstRaisedAt { get; set; }

    public int Level { get; set; }

    public int AssessedEntryRevision { get; set; }

    public Guid? CurrentAssessmentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

internal sealed class Finding : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid AlertId { get; set; }

    public Guid EntryId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ComponentId { get; set; }

    public Guid? RemediationId { get; set; }

    public string Exploitation { get; set; } = "none";

    public bool FixAvailable { get; set; }

    public int? ClosedLevel { get; set; }

    public string? ClosedVersionVerdict { get; set; }

    public required string MatchType { get; set; }

    public string? VersionVerdict { get; set; }

    public string? VersionReason { get; set; }

    public string? FixedVersion { get; set; }

    public required string Verdict { get; set; }

    public string VerdictSource { get; set; } = "rules";

    public int Level { get; set; }

    public required JsonDocument Evidence { get; set; }

    public required JsonDocument Factors { get; set; }

    public required string Status { get; set; }

    public string? StatusReason { get; set; }

    public Guid? StatusChangedBy { get; set; }

    public DateTimeOffset StatusChangedAt { get; set; }

    public string? StatusNote { get; set; }

    public bool IsBackfill { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

internal sealed class Remediation : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ComponentId { get; set; }

    public int Number { get; set; }

    public int Level { get; set; }

    public string? TargetVersion { get; set; }

    public int OpenFindings { get; set; }

    public string Exploitation { get; set; } = "none";

    public bool FixAvailable { get; set; }

    public required string Status { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset? LevelRaisedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public string? CloseReason { get; set; }

    public bool IsBackfill { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

internal sealed class AssessEvent : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? RemediationId { get; set; }

    public Guid? FindingId { get; set; }

    public Guid? ProjectId { get; set; }

    public Guid? NoticeId { get; set; }

    public DateTimeOffset At { get; set; }

    public required string Kind { get; set; }

    public Guid? Actor { get; set; }

    public required JsonDocument Data { get; set; }
}

internal sealed class Notice : IOrganizationOwned
{
    public const string Open = "open";
    public const string AffectsUs = "affects_us";
    public const string NotAffected = "not_affected";
    public const string Withdrawn = "withdrawn";

    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid EntryId { get; set; }

    public required string EntryKey { get; set; }

    public required string State { get; set; }

    public Guid[] ComponentIds { get; set; } = [];

    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecidedExploitation { get; set; }

    public bool DecidedKev { get; set; }

    public Guid? RemediationId { get; set; }

    public DateTimeOffset? ReappearedAt { get; set; }

    public string? ReappearedReason { get; set; }

    public double? CvssScore { get; set; }

    public string Exploitation { get; set; } = "none";

    public bool Exploited { get; private set; }

    public double ScoreRank { get; private set; }

    public bool IsBackfill { get; set; }

    public DateTimeOffset RaisedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

internal sealed class OrgCounter : IOrganizationOwned
{
    public Guid OrganizationId { get; set; }

    public int NextNumber { get; set; }
}

internal sealed class MatchScope : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? RepositoryLinkId { get; set; }

    public DateTimeOffset MatchedAt { get; set; }
}

internal sealed class AlertAssessment : IOrganizationOwned
{
    public const string RulesEngine = "rules";
    public const string ModelEngine = "rules+model";

    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid AlertId { get; set; }

    public int EntryRevision { get; set; }

    public required string InputFingerprint { get; set; }

    public required string Engine { get; set; }

    public int RulesetVersion { get; set; }

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public string? Language { get; set; }

    public required JsonDocument Output { get; set; }

    public int Attempts { get; set; }

    public int TokensIn { get; set; }

    public int TokensOut { get; set; }

    public int DurationMs { get; set; }

    public Guid? RequestedBy { get; set; }

    public Guid[] RemediationIds { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class AssessmentFeedback : IOrganizationOwned
{
    public const string Confirmed = "confirmed";
    public const string Corrected = "corrected";

    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid FindingId { get; set; }

    public Guid AlertId { get; set; }

    public Guid? AssessmentId { get; set; }

    public required string Kind { get; set; }

    public required string VerdictSource { get; set; }

    public required string VerdictBefore { get; set; }

    public string? Verdict { get; set; }

    public int LevelBefore { get; set; }

    public int? ExpectedLevel { get; set; }

    public string? Reason { get; set; }

    public Guid Actor { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
