using System.Text.Json;

namespace Ardea.Assessment.Contracts;

/// <summary>
/// Told about the events of master plan 8.2 (<c>action.*</c>, <c>backfill.completed</c>, and the notices' <c>notice.raised</c>,
/// <c>notice.reappeared</c> and <c>notice.backfilled</c>) a unit of work wrote,
/// inside that unit of work right before it commits: a subscriber's follow-up job exists exactly when the
/// event does (master plan 3.3). The Notifications module subscribes; Assessment knows nobody.
/// </summary>
public interface IActionEventSubscriber
{
    Task OnActionEventsAsync(IReadOnlyList<NotifiableEvent> events, CancellationToken cancellationToken);
}

/// <summary>An event of master plan 8.2 as history row: the row's id is the event's id.</summary>
/// <param name="Id">The history row, which is the event.</param>
/// <param name="Kind">E.g. <c>action.level_raised</c> or <c>backfill.completed</c>.</param>
/// <param name="At">When it happened.</param>
public sealed record NotifiableEvent(Guid Id, string Kind, DateTimeOffset At);

/// <summary>
/// Read access to events and actions for the notifications (master plan 8.2, 8.5), inside the unit of work
/// of the current organisation. Names of projects and components come from Assets, titles from Intel.
/// </summary>
public interface IActionFacts
{
    /// <summary>The events among the given history rows; unknown ids are left out.</summary>
    Task<IReadOnlyList<ActionEventFacts>> EventsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken);

    /// <summary>The actions with what a message says about them; unknown ids are left out.</summary>
    Task<IReadOnlyList<ActionFacts>> ActionsAsync(IReadOnlyCollection<Guid> remediationIds, CancellationToken cancellationToken);

    /// <summary>The open actions of a project that a first fill opened (master plan 7.7, 8.4).</summary>
    Task<IReadOnlyList<ActionFacts>> BackfillActionsAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>The names of those projects that exist in the organisation; others are left out.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ProjectNamesAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);

    /// <summary>How many findings joined each action in the period, without raising it (the digest's «more findings»).</summary>
    Task<IReadOnlyList<FindingGrowth>> FindingsJoinedAsync(DateTimeOffset after, DateTimeOffset until, CancellationToken cancellationToken);

    /// <summary>The notices with what a message says about them (master plan 17.3, ADR 048); unknown ids are left out.</summary>
    Task<IReadOnlyList<NoticeFacts>> NoticesAsync(IReadOnlyCollection<Guid> noticeIds, CancellationToken cancellationToken);

    /// <summary>The open reminders from a level on, for the reminders by level (master plan 8.7, 17.5; ADR 048).</summary>
    Task<IReadOnlyList<OpenReminder>> OpenRemindersAsync(int minLevel, CancellationToken cancellationToken);
}

/// <summary>An event with its subject.</summary>
/// <param name="Id">The event, which is its history row.</param>
/// <param name="Kind">The kind of master plan 8.2.</param>
/// <param name="At">When it happened.</param>
/// <param name="RemediationId">The action, for <c>action.*</c>.</param>
/// <param name="ProjectId">The project, for <c>backfill.completed</c>.</param>
/// <param name="Actor">The person who acted; <c>null</c> for the system.</param>
/// <param name="Data">What changed, e.g. <c>{"from": 4, "to": 5}</c>.</param>
/// <param name="NoticeId">The notice, for <c>notice.raised</c> and <c>notice.reappeared</c>.</param>
public sealed record ActionEventFacts(Guid Id, string Kind, DateTimeOffset At, Guid? RemediationId, Guid? ProjectId, Guid? Actor, JsonElement Data, Guid? NoticeId = null);

/// <summary>An action as a message describes it (master plan 8.5).</summary>
public sealed record ActionFacts
{
    public required Guid Id { get; init; }

    public required int Number { get; init; }

    /// <summary>2 to 5 while open; kept when it closes.</summary>
    public required int Level { get; init; }

    /// <summary><c>open</c> or <c>closed</c>.</summary>
    public required string Status { get; init; }

    public required Guid ProjectId { get; init; }

    public required string ProjectName { get; init; }

    public required string ComponentName { get; init; }

    public string? ComponentVersion { get; init; }

    public required int OpenFindings { get; init; }

    /// <summary><c>none</c>, <c>poc</c> or <c>active</c>.</summary>
    public required string Exploitation { get; init; }

    public required bool FixAvailable { get; init; }

    public string? TargetVersion { get; init; }

    public required bool IsBackfill { get; init; }

    public string? CloseReason { get; init; }

    /// <summary>Up to three of its open findings' entries, highest level first.</summary>
    public required IReadOnlyList<ActionEntryFacts> Entries { get; init; }
}

/// <param name="Key">The entry's key, e.g. <c>CVE-2026-115731</c>.</param>
/// <param name="Title">Its title.</param>
/// <param name="Level">The level of the action's finding for it.</param>
/// <param name="Exploitation"><c>none</c>, <c>poc</c> or <c>active</c>.</param>
public sealed record ActionEntryFacts(string Key, string? Title, int Level, string Exploitation);

/// <param name="RemediationId">The action.</param>
/// <param name="Joined">How many findings joined it.</param>
public sealed record FindingGrowth(Guid RemediationId, int Joined);

/// <summary>A notice as a message describes it (master plan 17.3, ADR 048): «could affect you», with its reason.</summary>
public sealed record NoticeFacts
{
    public required Guid Id { get; init; }

    /// <summary><c>open</c>, <c>affects_us</c>, <c>not_affected</c> or <c>withdrawn</c>.</summary>
    public required string State { get; init; }

    public required string EntryKey { get; init; }

    public string? EntryTitle { get; init; }

    /// <summary>The entry's CVSS base score; <c>null</c> when the sources give none.</summary>
    public double? CvssScore { get; init; }

    /// <summary><c>none</c>, <c>poc</c> or <c>active</c>.</summary>
    public required string Exploitation { get; init; }

    /// <summary>CISA lists the entry in its KEV catalogue.</summary>
    public required bool InKev { get; init; }

    /// <summary>Why an answered «doesn't affect us» came back: <c>exploited</c> or <c>kev</c>; <c>null</c> otherwise.</summary>
    public string? ReappearedReason { get; init; }

    /// <summary>The coarse components that raised it: the reason «Your stack names PostgreSQL».</summary>
    public required IReadOnlyList<NoticeComponentFacts> Components { get; init; }

    /// <summary>Actively exploited or listed in KEV: the notices that reach people at once by default (master plan 8.2).</summary>
    public bool Important => Exploitation == "active" || InKev;
}

/// <param name="Name">The product's name.</param>
/// <param name="Version">Its version line, or <c>null</c>.</param>
/// <param name="ProjectId">Its project; the stack is one too.</param>
/// <param name="ProjectName">The project's name.</param>
/// <param name="InStack">An entry of the organisation's stack.</param>
public sealed record NoticeComponentFacts(string Name, string? Version, Guid ProjectId, string ProjectName, bool InStack);

/// <summary>An open reminder as the reminders by level need it (master plan 8.7, ADR 048).</summary>
/// <param name="Id">The reminder.</param>
/// <param name="Level">Its level, 2 to 5.</param>
/// <param name="ProjectId">Its project, for a rule's project scope.</param>
/// <param name="Since">Since when it stands at its level, as far as the reminder knows: opened, or last raised.</param>
public sealed record OpenReminder(Guid Id, int Level, Guid ProjectId, DateTimeOffset Since);
