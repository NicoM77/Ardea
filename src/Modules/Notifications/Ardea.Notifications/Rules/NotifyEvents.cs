namespace Ardea.Notifications.Rules;

internal static class NotifyEvents
{
    public const string ActionRaised = "action.raised";
    public const string ActionLevelRaised = "action.level_raised";
    public const string ActionExploitationEscalated = "action.exploitation_escalated";
    public const string ActionFixAvailable = "action.fix_available";
    public const string ActionLevelLowered = "action.level_lowered";
    public const string ActionWithdrawn = "action.withdrawn";
    public const string ActionStatusChanged = "action.status_changed";
    public const string ActionNoteAdded = "action.note_added";
    public const string WarningPublished = "warning.published";
    public const string BackfillCompleted = "backfill.completed";

    public const string RepositoryUnavailable = "repository.unavailable";

    public const string NoticeRaised = "notice.raised";

    public const string NoticeReappeared = "notice.reappeared";

    public const string NoticeExploited = "notice.exploited";

    public const string NoticeBackfilled = "notice.backfilled";

    public const string DeliveryFailed = "delivery.failed";

    public const string ReminderDue = "reminder.due";

    public static readonly IReadOnlyList<string> All =
    [
        ActionRaised, ActionLevelRaised, ActionExploitationEscalated, ActionFixAvailable, ActionLevelLowered, ActionWithdrawn,
        ActionStatusChanged, ActionNoteAdded, WarningPublished, NoticeRaised, BackfillCompleted, RepositoryUnavailable,
    ];

    public static string RuleEvent(string kind) => kind is NoticeReappeared or NoticeExploited or NoticeBackfilled ? NoticeRaised : kind;

    public static bool IsNotice(string kind) => kind is NoticeRaised or NoticeReappeared or NoticeExploited or NoticeBackfilled;

    public static bool AllowedOn(string kind, string channel) =>
        kind is not (ActionStatusChanged or ActionNoteAdded) || channel is Channels.InApp or Channels.Webhook;

    public static bool IsAllClear(string kind) => kind is ActionLevelLowered or ActionWithdrawn;
}

internal static class Channels
{
    public const string InApp = "in_app";
    public const string Email = "email";
    public const string Webhook = "webhook";

    public const string Push = "push";

    public static readonly IReadOnlyList<string> Usable = [InApp, Email, Webhook, Push];
}

internal static class RecipientKinds
{
    public const string Role = "role";
    public const string User = "user";
    public const string Address = "address";
    public const string Endpoint = "endpoint";

    public static readonly IReadOnlyList<string> All = [Role, User, Address, Endpoint];

    public static bool Reaches(string kind, string channel) => kind switch
    {
        Role or User => channel is Channels.InApp or Channels.Email or Channels.Push,
        Address => channel == Channels.Email,
        Endpoint => channel == Channels.Webhook,
        _ => false,
    };
}

internal static class RecipientKeys
{
    public static string User(Guid id) => $"user:{id}";

    public static string Address(Guid id) => $"address:{id}";

    public static string Endpoint(Guid id) => $"endpoint:{id}";

    public static (string Kind, Guid Id) Parse(string key)
    {
        var colon = key.IndexOf(':', StringComparison.Ordinal);
        return (key[..colon], Guid.Parse(key[(colon + 1)..]));
    }
}

internal static class Exploitation
{
    public static int Rank(string? value) => value switch
    {
        "active" => 2,
        "poc" => 1,
        _ => 0,
    };
}
