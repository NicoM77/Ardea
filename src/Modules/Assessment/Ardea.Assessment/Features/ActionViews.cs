using System.Text.Json;

namespace Ardea.Assessment.Features;

/// <summary>A page of a list (master plan 10.1): <c>nextCursor</c> is empty on the last page.</summary>
internal sealed record ListPage<T>(IReadOnlyList<T> Items, string? NextCursor);

internal sealed record ActionProjectRef(Guid Id, string Name);

/// <summary>The component of an action or finding; <c>removed</c> once it left the profile.</summary>
internal sealed record ActionComponentRef(Guid Id, string Name, string? Version, bool Removed);

internal sealed record ActionEntryRef(Guid Id, string Key, string? Title);

/// <summary>An action in lists (master plan 7.4, 10.2 «Massnahmen und Meldungen»).</summary>
/// <param name="Number">The organisation's number, shared with alerts.</param>
/// <param name="Level">2 to 5; the maximum over its open findings.</param>
/// <param name="LevelName"><c>critical</c>, <c>high</c>, <c>medium</c>, <c>low</c>.</param>
/// <param name="Status"><c>open</c> or <c>closed</c> (master plan 17.5: open and done).</param>
/// <param name="Project">The project of the component.</param>
/// <param name="Component">The component the action is about.</param>
/// <param name="TargetVersion">The highest fix version of its open findings.</param>
/// <param name="OpenFindings">How many findings are open.</param>
/// <param name="Exploitation">The highest exploitation over its open findings.</param>
/// <param name="FixAvailable">One of its entries names a fix.</param>
/// <param name="OpenedAt">When it opened.</param>
/// <param name="LevelRaisedAt">When its level last rose.</param>
/// <param name="ClosedAt">When it closed.</param>
/// <param name="CloseReason">The most frequent outcome of its findings.</param>
/// <param name="IsBackfill">Opened by a first fill.</param>
/// <param name="Entries">The keys of up to three of its findings, highest level first.</param>
internal sealed record ActionSummary(
    int Number,
    int Level,
    string LevelName,
    string Status,
    ActionProjectRef Project,
    ActionComponentRef Component,
    string? TargetVersion,
    int OpenFindings,
    string Exploitation,
    bool FixAvailable,
    DateTimeOffset OpenedAt,
    DateTimeOffset? LevelRaisedAt,
    DateTimeOffset? ClosedAt,
    string? CloseReason,
    bool IsBackfill,
    IReadOnlyList<string> Entries);

/// <summary>One finding: a component affected by an entry, with the match, the version check and the rule set's factors.</summary>
internal sealed record FindingView(
    Guid Id,
    int? AlertNumber,
    int? ActionNumber,
    ActionEntryRef Entry,
    ActionProjectRef Project,
    ActionComponentRef Component,
    int Level,
    string LevelName,
    string Status,
    string? StatusReason,
    string? StatusNote,
    Guid? StatusChangedBy,
    DateTimeOffset StatusChangedAt,
    string MatchType,
    string? VersionVerdict,
    string? VersionReason,
    string? FixedVersion,
    string Verdict,
    string VerdictSource,
    string Exploitation,
    bool FixAvailable,
    bool IsBackfill,
    JsonElement Factors,
    JsonElement Evidence,
    FindingReviewView? Review = null);

/// <summary>A person's latest review of the finding's assessment (task 4.7).</summary>
/// <param name="Decision"><c>confirmed</c> or <c>corrected</c>.</param>
/// <param name="By">The person.</param>
/// <param name="At">When.</param>
/// <param name="VerdictSource">Whose verdict the person reviewed: <c>rules</c>, <c>model</c> or <c>human</c>.</param>
/// <param name="VerdictBefore">The verdict the person reviewed.</param>
/// <param name="Verdict">The person's verdict; <c>null</c> when only the level was questioned.</param>
/// <param name="LevelBefore">The level the person saw.</param>
/// <param name="ExpectedLevel">The level the person expects instead; feedback for the rule set and the model, the rules keep setting the level.</param>
/// <param name="Reason">Why.</param>
internal sealed record FindingReviewView(
    string Decision, Guid By, DateTimeOffset At, string VerdictSource, string VerdictBefore, string? Verdict, int LevelBefore, int? ExpectedLevel, string? Reason);

/// <summary>A row of the history; its id is the event's id for the notifications (master plan 8.2).</summary>
internal sealed record ActionEventView(Guid Id, string Kind, DateTimeOffset At, Guid? Actor, Guid? FindingId, JsonElement Data);

/// <summary>An action with its findings, sorted by level, and its history.</summary>
internal sealed record ActionDetailView(ActionSummary Action, AnswersView? Answers, IReadOnlyList<FindingView> Findings, IReadOnlyList<ActionEventView> Events);

/// <summary>A vulnerability from the organisation's point of view, in lists.</summary>
internal sealed record AlertSummary(int Number, int Level, string LevelName, ActionEntryRef Entry, int OpenActions, int Findings, DateTimeOffset? FirstRaisedAt, DateTimeOffset ChangedAt);

/// <summary>What the entry says, as far as the detail shows it (master plan 10.3).</summary>
internal sealed record AlertEntryView(
    Guid Id, string Key, string? Title, string State, DateTimeOffset? PublishedAt, DateTimeOffset ChangedAt, double? CvssScore, string? CvssVersion,
    string? CvssVector, string Exploitation, string? ExploitationSource, DateTimeOffset? ExploitationSince, bool FixAvailable, string? EpssClass, bool AuthorityWarning);

/// <summary>An alert with the actions it belongs to and its findings with factors per component (master plan 10.3).</summary>
internal sealed record AlertDetailView(AlertSummary Alert, AlertEntryView Entry, AnswersView? Answers, IReadOnlyList<ActionSummary> Actions, IReadOnlyList<FindingView> Findings);

/// <summary>An urgent action on the overview with its three answers (master plan 11.2: the answers come first).</summary>
internal sealed record UrgentActionView(ActionSummary Action, AnswersView Answers);

internal sealed record LevelCountView(int Level, string LevelName, int Actions);

internal sealed record ProjectLevelView(ActionProjectRef Project, int Level, string LevelName, int Actions);

/// <summary>Events of the last 24 hours by kind.</summary>
internal sealed record ChangeCountView(string Kind, int Count);

/// <summary>Everything the overview needs in one call (master plan 10.2): counts per level, the most urgent actions, level per project, recent changes.</summary>
internal sealed record OverviewView(
    IReadOnlyList<LevelCountView> OpenByLevel, IReadOnlyList<UrgentActionView> Urgent, IReadOnlyList<ProjectLevelView> Projects, IReadOnlyList<ChangeCountView> Last24Hours);

/// <summary>
/// Sets the outcome of an action for all its open findings (master plan 7.4, 17.5): <c>remediated</c>,
/// <c>risk_accepted</c> or <c>not_affected</c>; <c>open</c> on a closed action takes back its closing.
/// <c>expectedStatus</c> is the status the caller saw, <c>open</c> or <c>closed</c> (master plan 10.1).
/// </summary>
internal sealed record SetActionStatusRequest(string? Status, string? ExpectedStatus, string? Note);

internal sealed record AddNoteRequest(string? Text);

/// <summary>Sets the outcome of one finding, or <c>open</c> to take it back; <c>expectedStatus</c> as for actions.</summary>
internal sealed record SetFindingStatusRequest(string? Status, string? ExpectedStatus, string? Note);

/// <summary>
/// A person's review of a finding's assessment (task 4.7). <c>decision</c> <c>confirm</c> takes the model's estimate as
/// a person's verdict; <c>correct</c> names another <c>verdict</c>, the <c>level</c> the person expects, or both, with a
/// <c>reason</c>. <c>expectedVerdict</c> and <c>expectedSource</c> are the verdict and its source the caller saw.
/// </summary>
internal sealed record ReviewVerdictRequest(string? Decision, string? ExpectedVerdict, string? ExpectedSource, string? Verdict, int? Level, string? Reason);

/// <summary>The answer to a request for the model's assessment of an alert.</summary>
/// <param name="Queued"><c>false</c> when an assessment of the alert was already waiting; it covers this request.</param>
internal sealed record AssessmentRequestView(bool Queued);

internal static class Levels
{
    public static string Name(int level) => level switch
    {
        5 => "critical",
        4 => "high",
        3 => "medium",
        2 => "low",
        _ => "not_relevant",
    };
}
