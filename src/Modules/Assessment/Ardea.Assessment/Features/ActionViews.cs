using System.Text.Json;

namespace Ardea.Assessment.Features;

internal sealed record ListPage<T>(IReadOnlyList<T> Items, string? NextCursor);

internal sealed record ActionProjectRef(Guid Id, string Name);

internal sealed record ActionComponentRef(Guid Id, string Name, string? Version, bool Removed);

internal sealed record ActionEntryRef(Guid Id, string Key, string? Title);

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

internal sealed record FindingReviewView(
    string Decision, Guid By, DateTimeOffset At, string VerdictSource, string VerdictBefore, string? Verdict, int LevelBefore, int? ExpectedLevel, string? Reason);

internal sealed record ActionEventView(Guid Id, string Kind, DateTimeOffset At, Guid? Actor, Guid? FindingId, JsonElement Data);

internal sealed record ActionDetailView(ActionSummary Action, AnswersView? Answers, IReadOnlyList<FindingView> Findings, IReadOnlyList<ActionEventView> Events);

internal sealed record AlertSummary(int Number, int Level, string LevelName, ActionEntryRef Entry, int OpenActions, int Findings, DateTimeOffset? FirstRaisedAt, DateTimeOffset ChangedAt);

internal sealed record AlertEntryView(
    Guid Id, string Key, string? Title, string State, DateTimeOffset? PublishedAt, DateTimeOffset ChangedAt, double? CvssScore, string? CvssVersion,
    string? CvssVector, string Exploitation, string? ExploitationSource, DateTimeOffset? ExploitationSince, bool FixAvailable, string? EpssClass, bool AuthorityWarning);

internal sealed record AlertDetailView(
    AlertSummary Alert, AlertEntryView Entry, AnswersView? Answers, IReadOnlyList<ActionSummary> Actions, IReadOnlyList<FindingView> Findings, bool AssessmentAvailable);

internal sealed record UrgentActionView(ActionSummary Action, AnswersView Answers);

internal sealed record LevelCountView(int Level, string LevelName, int Actions);

internal sealed record ProjectLevelView(ActionProjectRef Project, int Level, string LevelName, int Actions);

internal sealed record ChangeCountView(string Kind, int Count);

internal sealed record OverviewView(
    IReadOnlyList<LevelCountView> OpenByLevel, IReadOnlyList<UrgentActionView> Urgent, IReadOnlyList<ProjectLevelView> Projects, IReadOnlyList<ChangeCountView> Last24Hours);

internal sealed record SetActionStatusRequest(string? Status, string? ExpectedStatus, string? Note);

internal sealed record AddNoteRequest(string? Text);

internal sealed record SetFindingStatusRequest(string? Status, string? ExpectedStatus, string? Note);

internal sealed record ReviewVerdictRequest(string? Decision, string? ExpectedVerdict, string? ExpectedSource, string? Verdict, int? Level, string? Reason);

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
