using System.Text.Json;
using Ardea.Assessment.Contracts;
using Ardea.Iam.Contracts;

namespace Ardea.Notifications.Messages;

/// <summary>A warning without identifier (master plan 8.2) as a message names it.</summary>
internal sealed record WarningFacts(Guid EntryId, string Key, string? Title, DateTimeOffset? PublishedAt);

/// <summary>A finished first fill of a project (master plan 7.7): one summary instead of single alarms.</summary>
internal sealed record BackfillFacts(Guid ProjectId, string ProjectName, int Actions, int? HighestLevel, int Findings);

/// <summary>Earlier notices a reverse search raised at once (ADR 047, 048): one summary instead of one message each.</summary>
/// <param name="ProjectId">The project whose reverse search raised them, usually the stack.</param>
/// <param name="ProjectName">Its name.</param>
/// <param name="Notices">How many.</param>
/// <param name="Important">Of them, actively exploited or listed in KEV.</param>
internal sealed record NoticeBackfillFacts(Guid ProjectId, string ProjectName, int Notices, int Important);

/// <summary>One event in a message, or one line of a digest.</summary>
internal sealed record MessageItem
{
    /// <summary>The level a notice meets a rule's threshold at (ADR 048): it has no level of its own (master plan 17.3).</summary>
    public const int ImportantNoticeLevel = 5;

    /// <summary>A notice that is neither exploited nor in KEV meets only the lowest threshold, «low and up».</summary>
    public const int NoticeLevel = 2;

    public required Guid EventId { get; init; }

    public required string Event { get; init; }

    public required DateTimeOffset At { get; init; }

    /// <summary>The level the event concerns: the action's, or the highest of a first fill; <c>null</c> for warnings.</summary>
    public int? Level { get; init; }

    public ActionFacts? Action { get; init; }

    public WarningFacts? Warning { get; init; }

    public BackfillFacts? Backfill { get; init; }

    /// <summary>The notice of <c>notice.raised</c> and <c>notice.reappeared</c>.</summary>
    public NoticeFacts? Notice { get; init; }

    /// <summary>The summary of <c>notice.backfilled</c>.</summary>
    public NoticeBackfillFacts? NoticeBackfill { get; init; }

    /// <summary>
    /// The level the item meets a rule's threshold at: its own, or for a notice the stand-in of ADR 048 (exploited or in
    /// KEV meets every threshold, else level 2); <c>null</c> for a warning, which meets every rule that names it.
    /// </summary>
    public int? ThresholdLevel =>
        Notice is { } notice ? (notice.Important ? ImportantNoticeLevel : NoticeLevel)
        : NoticeBackfill is { } backfill ? (backfill.Important > 0 ? ImportantNoticeLevel : NoticeLevel)
        : Level ?? (Backfill is not null ? 1 : null);

    /// <summary>The projects the item is about, for a rule's scope; a notice is about the projects of its components.</summary>
    public IReadOnlyList<Guid> Projects =>
        Action is { } action ? [action.ProjectId]
        : Backfill is { } backfill ? [backfill.ProjectId]
        : Notice is { } notice ? [.. notice.Components.Select(c => c.ProjectId).Distinct()]
        : NoticeBackfill is { } summary ? [summary.ProjectId]
        : [];

    /// <summary>The person whose change it is; <c>null</c> for the system.</summary>
    public Guid? Actor { get; init; }

    /// <summary>The event's data, e.g. <c>{"from": 4, "to": 5}</c>.</summary>
    public JsonElement Data { get; init; }

    /// <summary>In a digest: the recipient already heard about it on this channel at once (master plan 8.4).</summary>
    public bool AlreadyReported { get; init; }

    /// <summary>In a digest: findings that joined the action in the period without raising it.</summary>
    public int NewFindings { get; init; }

    public int? DataInt(string name) =>
        Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    public string? DataString(string name) =>
        Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// The item of a notice's event (ADR 048): the notice of <c>notice.raised</c> or <c>notice.reappeared</c>, or the summary of
    /// <c>notice.backfilled</c>; <c>null</c> when the notice is gone.
    /// </summary>
    public static MessageItem? ForNotice(ActionEventFacts fact, IReadOnlyDictionary<Guid, NoticeFacts> notices, IReadOnlyDictionary<Guid, string> projectNames)
    {
        var item = new MessageItem { EventId = fact.Id, Event = fact.Kind, At = fact.At, Data = fact.Data, Actor = fact.Actor };
        if (fact.Kind == Rules.NotifyEvents.NoticeBackfilled)
        {
            return fact.ProjectId is { } project
                ? item with
                {
                    NoticeBackfill = new NoticeBackfillFacts(project, projectNames.GetValueOrDefault(project, string.Empty), item.DataInt("notices") ?? 0, item.DataInt("important") ?? 0),
                }
                : null;
        }

        return fact.NoticeId is { } id && notices.TryGetValue(id, out var notice) ? item with { Notice = notice } : null;
    }
}

/// <summary>Everything a channel needs to word and send one message; read before the call goes out.</summary>
internal sealed record MessageContent
{
    public required Guid DeliveryId { get; init; }

    /// <summary><c>immediate</c>, <c>digest</c> or <c>test</c>.</summary>
    public required string Kind { get; init; }

    public required OrganizationProfile Organization { get; init; }

    public required string Locale { get; init; }

    public required Uri PublicUrl { get; init; }

    public required IReadOnlyList<MessageItem> Items { get; init; }

    public DateTimeOffset? PeriodStart { get; init; }

    public DateTimeOffset? PeriodEnd { get; init; }

    /// <summary>The highest level among the items.</summary>
    public int? Level => Items.Max(i => i.Level);

    public Uri Link(string relative) => new(PublicUrl, relative.TrimStart('/'));

    /// <summary>The reminder's page (task 5a.5); the API and the events keep the name «action» (ADR 046).</summary>
    public static string ReminderPath(OrganizationProfile organization, int number) =>
        $"/o/{organization.Slug}/reminders/{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public static string RemindersPath(OrganizationProfile organization) => $"/o/{organization.Slug}/reminders";

    /// <summary>The Notices page (task 5a.5), where a notice is answered.</summary>
    public static string NoticesPath(OrganizationProfile organization) => $"/o/{organization.Slug}/notices";

    public static string WarningPath(string key) => $"/entries/{Uri.EscapeDataString(key)}";

    public static string ProjectPath(OrganizationProfile organization, Guid project) => $"/o/{organization.Slug}/projects/{project}";

    /// <summary>The page an item opens.</summary>
    public string? PathOf(MessageItem item) =>
        item.Action is { } action ? ReminderPath(Organization, action.Number)
        : item.Notice is not null || item.NoticeBackfill is not null ? NoticesPath(Organization)
        : item.Warning is { } warning ? WarningPath(warning.Key)
        : item.Backfill is { } backfill ? ProjectPath(Organization, backfill.ProjectId)
        : null;
}
