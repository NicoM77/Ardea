using System.Text.Json;
using Ardea.Platform.Persistence;

namespace Ardea.Assessment.Persistence;

/// <summary>A vulnerability from the point of view of one organisation (master plan 4.6, 7.4).</summary>
internal sealed class Alert : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid EntryId { get; set; }

    /// <summary>The entry's primary key, e.g. <c>CVE-2026-115731</c>, kept for lists and search.</summary>
    public required string EntryKey { get; set; }

    /// <summary>From the organisation's counter, once one of its findings reaches level 2 (master plan 4.6).</summary>
    public int? Number { get; set; }

    public DateTimeOffset? FirstRaisedAt { get; set; }

    /// <summary>The maximum over its findings; 1 while none of them is relevant.</summary>
    public int Level { get; set; }

    /// <summary>The entry's revision the findings were last computed from.</summary>
    public int AssessedEntryRevision { get; set; }

    /// <summary>
    /// The latest assessment with a language model (master plan 4.6): a successful one, or a failed attempt that
    /// gives the note «ohne KI-Einschätzung». <c>null</c> while the model never looked at the alert.
    /// </summary>
    public Guid? CurrentAssessmentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>
/// One component affected by one entry (master plan 4.6, 7.2, 7.3). Weak matches and versions outside
/// the affected ranges are stored too, as level 1 with status <c>not_affected</c>: they answer why an
/// alert did not come.
/// </summary>
internal sealed class Finding : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid AlertId { get; set; }

    public Guid EntryId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ComponentId { get; set; }

    /// <summary>The action the finding belongs to; <c>null</c> at level 1 (master plan 4.6, 7.4).</summary>
    public Guid? RemediationId { get; set; }

    /// <summary>The entry's exploitation when last assessed: <c>none</c>, <c>poc</c>, <c>active</c>.</summary>
    public string Exploitation { get; set; } = "none";

    /// <summary>The entry names a fix (master plan 6.3).</summary>
    public bool FixAvailable { get; set; }

    /// <summary>The level when a person closed the finding; a reassessment reopens it only above (master plan 6.7).</summary>
    public int? ClosedLevel { get; set; }

    /// <summary>The version verdict when a person closed the finding; «in range» again reopens it (master plan 6.7).</summary>
    public string? ClosedVersionVerdict { get; set; }

    /// <summary>
    /// How the entry hit the component (master plan 7.2): strong <c>package</c>, <c>cpe</c>, <c>product</c>;
    /// weak <c>variant</c>, <c>ecosystem</c>, <c>name</c>.
    /// </summary>
    public required string MatchType { get; set; }

    /// <summary><c>in_range</c>, <c>out_of_range</c> or <c>unknown</c>; <c>null</c> for a weak match, which is not checked.</summary>
    public string? VersionVerdict { get; set; }

    /// <summary>The rule that gave the verdict, e.g. <c>in_affected_range</c>, <c>above_all_ranges</c>, <c>distribution_package</c>.</summary>
    public string? VersionReason { get; set; }

    /// <summary>The fix version of the range the installed version lies in (master plan 7.4).</summary>
    public string? FixedVersion { get; set; }

    /// <summary><c>affected</c>, <c>likely</c>, <c>uncertain</c>, <c>unlikely</c> or <c>not_affected</c> (master plan 4.9).</summary>
    public required string Verdict { get; set; }

    /// <summary><c>rules</c>, <c>model</c> or <c>human</c>.</summary>
    public string VerdictSource { get; set; } = "rules";

    public int Level { get; set; }

    /// <summary>The match and the version check with their evidence: keys, installed version, ranges per source.</summary>
    public required JsonDocument Evidence { get; set; }

    /// <summary>
    /// How the rule set reached the level (master plan 7.6): its version, an exclusion, or the factors with
    /// value, evidence and points, the sum and the bounds that applied. Recomputable from the stored inputs.
    /// </summary>
    public required JsonDocument Factors { get; set; }

    /// <summary><c>open</c>, <c>in_review</c>, <c>remediated</c>, <c>risk_accepted</c> or <c>not_affected</c>.</summary>
    public required string Status { get; set; }

    /// <summary>
    /// Why the system set the status: <c>weak_match</c>, <c>out_of_range</c>, <c>withdrawn</c>, <c>merged</c>,
    /// <c>no_longer_matched</c>, <c>component_removed</c>, <c>project_archived</c>; <c>null</c> when a person set it.
    /// </summary>
    public string? StatusReason { get; set; }

    public Guid? StatusChangedBy { get; set; }

    public DateTimeOffset StatusChangedAt { get; set; }

    public string? StatusNote { get; set; }

    /// <summary>Found by the first reverse search of a project or a newly connected repository (master plan 7.7).</summary>
    public bool IsBackfill { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>
/// The action: all open findings of one component in one project (master plan 4.6, 7.4). At most one per
/// component is not closed; a later finding after closing opens a new one.
/// </summary>
internal sealed class Remediation : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ComponentId { get; set; }

    /// <summary>From the organisation's counter, shared with alerts.</summary>
    public int Number { get; set; }

    /// <summary>The maximum over its open findings; kept when it closes.</summary>
    public int Level { get; set; }

    /// <summary>The highest fix version of its open findings (master plan 7.4).</summary>
    public string? TargetVersion { get; set; }

    public int OpenFindings { get; set; }

    /// <summary>The highest exploitation over its open findings.</summary>
    public string Exploitation { get; set; } = "none";

    public bool FixAvailable { get; set; }

    /// <summary><c>open</c>, <c>in_review</c> or <c>closed</c>.</summary>
    public required string Status { get; set; }

    public Guid? AssignedTo { get; set; }

    /// <summary>Start plus the deadline of the current level (master plan 4.9); <c>null</c> once closed.</summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>A person set the deadline; the level no longer moves it.</summary>
    public bool DueAtManual { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset? LevelRaisedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>The most frequent outcome of its findings, e.g. <c>remediated</c>, <c>fixed_by_update</c>, <c>component_removed</c>.</summary>
    public string? CloseReason { get; set; }

    /// <summary>Opened by a first fill (master plan 7.7): no <c>action.raised</c>.</summary>
    public bool IsBackfill { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>
/// The history of actions and findings (master plan 4.6). The row's id is the event's id for the
/// notifications (master plan 8.2); <c>action.*</c> and <c>backfill.completed</c> are notification events.
/// </summary>
internal sealed class AssessEvent : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? RemediationId { get; set; }

    public Guid? FindingId { get; set; }

    /// <summary>The project, for <c>backfill.completed</c>.</summary>
    public Guid? ProjectId { get; set; }

    public DateTimeOffset At { get; set; }

    public required string Kind { get; set; }

    /// <summary>The person who acted; <c>null</c> for the system.</summary>
    public Guid? Actor { get; set; }

    public required JsonDocument Data { get; set; }
}

/// <summary>The organisation's number range for actions and alerts (master plan 4.9).</summary>
internal sealed class OrgCounter : IOrganizationOwned
{
    public Guid OrganizationId { get; set; }

    public int NextNumber { get; set; }
}

/// <summary>
/// What the reverse search has seen of a project: the project itself (<c>repository_link_id</c> empty)
/// and each repository link. A first look is a first fill (master plan 7.7).
/// </summary>
internal sealed class MatchScope : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? RepositoryLinkId { get; set; }

    public DateTimeOffset MatchedAt { get; set; }
}

/// <summary>
/// One immutable assessment of an alert over all its hit components (master plan 4.6, 7.5): what went in
/// (fingerprint, entry revision, rule set and prompt versions), which model answered, and its checked output.
/// A failed attempt is stored too, with <c>engine</c> <c>rules</c>: it carries the note «ohne KI-Einschätzung»
/// and its tokens count against the quotas. The database refuses updates.
/// </summary>
internal sealed class AlertAssessment : IOrganizationOwned
{
    public const string RulesEngine = "rules";
    public const string ModelEngine = "rules+model";

    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid AlertId { get; set; }

    public int EntryRevision { get; set; }

    /// <summary>A hash over entry revision, hit components with version and context, rule set and prompt version (master plan 4.9).</summary>
    public required string InputFingerprint { get; set; }

    /// <summary><c>rules+model</c> for a checked answer of the model; <c>rules</c> when the model failed twice.</summary>
    public required string Engine { get; set; }

    public int RulesetVersion { get; set; }

    /// <summary>The provider's name as configured, e.g. <c>local</c>.</summary>
    public string? Provider { get; set; }

    /// <summary>The model the provider was asked for, e.g. <c>qwen3:14b</c>.</summary>
    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    /// <summary>The language of the generated texts, the organisation's (master plan 7.8).</summary>
    public string? Language { get; set; }

    /// <summary>
    /// The checked output with real component ids and the numbered source list it refers to; for a failed
    /// attempt, the reason (<c>invalid_output</c>, <c>provider_error</c>, <c>timeout</c>) and no text of the model.
    /// </summary>
    public required JsonDocument Output { get; set; }

    /// <summary>Calls made: one, or two after a refused answer or a failed provider. The actions' quota counts them.</summary>
    public int Attempts { get; set; }

    /// <summary>Tokens sent; a call without a usage report is charged its estimate (ADR 037).</summary>
    public int TokensIn { get; set; }

    public int TokensOut { get; set; }

    public int DurationMs { get; set; }

    /// <summary>The person who asked for it; <c>null</c> when the system did, which counts against the actions' quota (master plan 7.5).</summary>
    public Guid? RequestedBy { get; set; }

    /// <summary>The open actions the assessment covered when it ran, for the quota of five per action in seven days.</summary>
    public Guid[] RemediationIds { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A person's review of a finding's assessment (master plan 7.7, 7.9 «Rückmeldung im Betrieb»; task 4.7): the
/// estimate confirmed, or the verdict or the level corrected with a reason. One row per review, never changed; the
/// rows of the model's verdicts give the correction rate per model and prompt version.
/// </summary>
internal sealed class AssessmentFeedback : IOrganizationOwned
{
    public const string Confirmed = "confirmed";
    public const string Corrected = "corrected";

    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid FindingId { get; set; }

    public Guid AlertId { get; set; }

    /// <summary>The model's assessment whose verdict the person reviewed; <c>null</c> when the verdict came from the rules or a person.</summary>
    public Guid? AssessmentId { get; set; }

    /// <summary><c>confirmed</c> or <c>corrected</c>.</summary>
    public required string Kind { get; set; }

    /// <summary>Whose verdict was reviewed: <c>rules</c>, <c>model</c> or <c>human</c>.</summary>
    public required string VerdictSource { get; set; }

    /// <summary>The verdict the person saw.</summary>
    public required string VerdictBefore { get; set; }

    /// <summary>The person's verdict; <c>null</c> when only the level was corrected.</summary>
    public string? Verdict { get; set; }

    /// <summary>The finding's level the person saw.</summary>
    public int LevelBefore { get; set; }

    /// <summary>The level the person expects instead (master plan 7.9 «falsche Stufe»); <c>null</c> when the level was not questioned.</summary>
    public int? ExpectedLevel { get; set; }

    /// <summary>Why; required for a correction.</summary>
    public string? Reason { get; set; }

    public Guid Actor { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
