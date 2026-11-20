using System.Text.Json;
using Ardea.Assessment.Contracts;
using Ardea.Iam.Contracts;

namespace Ardea.Notifications.Messages;

internal sealed record WarningFacts(Guid EntryId, string Key, string? Title, DateTimeOffset? PublishedAt);

internal sealed record BackfillFacts(Guid ProjectId, string ProjectName, int Actions, int? HighestLevel, int Findings);

internal sealed record NoticeBackfillFacts(Guid ProjectId, string ProjectName, int Notices, int Important);

internal sealed record MessageItem
{
    public const int ImportantNoticeLevel = 5;

    public const int NoticeLevel = 2;

    public required Guid EventId { get; init; }

    public required string Event { get; init; }

    public required DateTimeOffset At { get; init; }

    public int? Level { get; init; }

    public ActionFacts? Action { get; init; }

    public WarningFacts? Warning { get; init; }

    public BackfillFacts? Backfill { get; init; }

    public NoticeFacts? Notice { get; init; }

    public NoticeBackfillFacts? NoticeBackfill { get; init; }

    public int? ThresholdLevel =>
        Notice is { } notice ? (notice.Important ? ImportantNoticeLevel : NoticeLevel)
        : NoticeBackfill is { } backfill ? (backfill.Important > 0 ? ImportantNoticeLevel : NoticeLevel)
        : Level ?? (Backfill is not null ? 1 : null);

    public IReadOnlyList<Guid> Projects =>
        Action is { } action ? [action.ProjectId]
        : Backfill is { } backfill ? [backfill.ProjectId]
        : Notice is { } notice ? [.. notice.Components.Select(c => c.ProjectId).Distinct()]
        : NoticeBackfill is { } summary ? [summary.ProjectId]
        : [];

    public Guid? Actor { get; init; }

    public JsonElement Data { get; init; }

    public bool AlreadyReported { get; init; }

    public int NewFindings { get; init; }

    public int? DataInt(string name) =>
        Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    public string? DataString(string name) =>
        Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

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

internal sealed record MessageContent
{
    public required Guid DeliveryId { get; init; }

    public required string Kind { get; init; }

    public required OrganizationProfile Organization { get; init; }

    public required string Locale { get; init; }

    public required Uri PublicUrl { get; init; }

    public required IReadOnlyList<MessageItem> Items { get; init; }

    public DateTimeOffset? PeriodStart { get; init; }

    public DateTimeOffset? PeriodEnd { get; init; }

    public int? Level => Items.Max(i => i.Level);

    public Uri Link(string relative) => new(PublicUrl, relative.TrimStart('/'));

    public static string ReminderPath(OrganizationProfile organization, int number) =>
        $"/o/{organization.Slug}/reminders/{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public static string RemindersPath(OrganizationProfile organization) => $"/o/{organization.Slug}/reminders";

    public static string NoticesPath(OrganizationProfile organization) => $"/o/{organization.Slug}/notices";

    public static string WarningPath(string key) => $"/entries/{Uri.EscapeDataString(key)}";

    public static string ProjectPath(OrganizationProfile organization, Guid project) => $"/o/{organization.Slug}/projects/{project}";

    public string? PathOf(MessageItem item) =>
        item.Action is { } action ? ReminderPath(Organization, action.Number)
        : item.Notice is not null || item.NoticeBackfill is not null ? NoticesPath(Organization)
        : item.Warning is { } warning ? WarningPath(warning.Key)
        : item.Backfill is { } backfill ? ProjectPath(Organization, backfill.ProjectId)
        : null;
}
