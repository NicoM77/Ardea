using System.Text.Json;

namespace Ardea.Assessment.Contracts;

public interface IActionEventSubscriber
{
    Task OnActionEventsAsync(IReadOnlyList<NotifiableEvent> events, CancellationToken cancellationToken);
}

public sealed record NotifiableEvent(Guid Id, string Kind, DateTimeOffset At);

public interface IActionFacts
{
    Task<IReadOnlyList<ActionEventFacts>> EventsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActionFacts>> ActionsAsync(IReadOnlyCollection<Guid> remediationIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActionFacts>> BackfillActionsAsync(Guid projectId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> ProjectNamesAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<FindingGrowth>> FindingsJoinedAsync(DateTimeOffset after, DateTimeOffset until, CancellationToken cancellationToken);

    Task<IReadOnlyList<NoticeFacts>> NoticesAsync(IReadOnlyCollection<Guid> noticeIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<OpenReminder>> OpenRemindersAsync(int minLevel, CancellationToken cancellationToken);
}

public sealed record ActionEventFacts(Guid Id, string Kind, DateTimeOffset At, Guid? RemediationId, Guid? ProjectId, Guid? Actor, JsonElement Data, Guid? NoticeId = null);

public sealed record ActionFacts
{
    public required Guid Id { get; init; }

    public required int Number { get; init; }

    public required int Level { get; init; }

    public required string Status { get; init; }

    public required Guid ProjectId { get; init; }

    public required string ProjectName { get; init; }

    public required string ComponentName { get; init; }

    public string? ComponentVersion { get; init; }

    public required int OpenFindings { get; init; }

    public required string Exploitation { get; init; }

    public required bool FixAvailable { get; init; }

    public string? TargetVersion { get; init; }

    public required bool IsBackfill { get; init; }

    public string? CloseReason { get; init; }

    public required IReadOnlyList<ActionEntryFacts> Entries { get; init; }
}

public sealed record ActionEntryFacts(string Key, string? Title, int Level, string Exploitation);

public sealed record FindingGrowth(Guid RemediationId, int Joined);

public sealed record NoticeFacts
{
    public required Guid Id { get; init; }

    public required string State { get; init; }

    public required string EntryKey { get; init; }

    public string? EntryTitle { get; init; }

    public double? CvssScore { get; init; }

    public required string Exploitation { get; init; }

    public required bool InKev { get; init; }

    public string? ReappearedReason { get; init; }

    public required IReadOnlyList<NoticeComponentFacts> Components { get; init; }

    public bool Important => Exploitation == "active" || InKev;
}

public sealed record NoticeComponentFacts(string Name, string? Version, Guid ProjectId, string ProjectName, bool InStack);

public sealed record OpenReminder(Guid Id, int Level, Guid ProjectId, DateTimeOffset Since);
