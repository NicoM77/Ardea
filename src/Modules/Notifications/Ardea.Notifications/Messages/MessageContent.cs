using System.Text.Json;
using Ardea.Assessment.Contracts;
using Ardea.Iam.Contracts;

namespace Ardea.Notifications.Messages;

/// <summary>A warning without identifier (master plan 8.2) as a message names it.</summary>
internal sealed record WarningFacts(Guid EntryId, string Key, string? Title, DateTimeOffset? PublishedAt);

/// <summary>A finished first fill of a project (master plan 7.7): one summary instead of single alarms.</summary>
internal sealed record BackfillFacts(Guid ProjectId, string ProjectName, int Actions, int? HighestLevel, int Findings);

/// <summary>One event in a message, or one line of a digest.</summary>
internal sealed record MessageItem
{
    public required Guid EventId { get; init; }

    public required string Event { get; init; }

    public required DateTimeOffset At { get; init; }

    /// <summary>The level the event concerns: the action's, or the highest of a first fill; <c>null</c> for warnings.</summary>
    public int? Level { get; init; }

    public ActionFacts? Action { get; init; }

    public WarningFacts? Warning { get; init; }

    public BackfillFacts? Backfill { get; init; }

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

    public static string ActionPath(OrganizationProfile organization, int number) =>
        $"/o/{organization.Slug}/actions/{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public static string WarningPath(string key) => $"/entries/{Uri.EscapeDataString(key)}";

    public static string ProjectPath(OrganizationProfile organization, Guid project) => $"/o/{organization.Slug}/projects/{project}";

    /// <summary>The page an item opens.</summary>
    public string? PathOf(MessageItem item) =>
        item.Action is { } action ? ActionPath(Organization, action.Number)
        : item.Warning is { } warning ? WarningPath(warning.Key)
        : item.Backfill is { } backfill ? ProjectPath(Organization, backfill.ProjectId)
        : null;
}
