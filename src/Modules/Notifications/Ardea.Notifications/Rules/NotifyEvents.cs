namespace Ardea.Notifications.Rules;

/// <summary>The events of master plan 8.2, the only list of them.</summary>
internal static class NotifyEvents
{
    public const string ActionRaised = "action.raised";
    public const string ActionLevelRaised = "action.level_raised";
    public const string ActionExploitationEscalated = "action.exploitation_escalated";
    public const string ActionFixAvailable = "action.fix_available";
    public const string ActionLevelLowered = "action.level_lowered";
    public const string ActionWithdrawn = "action.withdrawn";
    public const string ActionAssigned = "action.assigned";
    public const string ActionStatusChanged = "action.status_changed";
    public const string ActionNoteAdded = "action.note_added";
    public const string WarningPublished = "warning.published";
    public const string BackfillCompleted = "backfill.completed";

    /// <summary>Raised by the repository indexing of master plan 16.6; rules may name it already.</summary>
    public const string RepositoryUnavailable = "repository.unavailable";

    /// <summary>The owners' in-app warning when level 5 failed on every channel of a recipient (master plan 8.6); not a rule event.</summary>
    public const string DeliveryFailed = "delivery.failed";

    public static readonly IReadOnlyList<string> All =
    [
        ActionRaised, ActionLevelRaised, ActionExploitationEscalated, ActionFixAvailable, ActionLevelLowered, ActionWithdrawn,
        ActionAssigned, ActionStatusChanged, ActionNoteAdded, WarningPublished, BackfillCompleted, RepositoryUnavailable,
    ];

    /// <summary>The work of the team reaches people in the app only (master plan 8.2); machines may listen through webhooks.</summary>
    public static bool AllowedOn(string kind, string channel) =>
        kind is not (ActionStatusChanged or ActionNoteAdded) || channel is Channels.InApp or Channels.Webhook;

    /// <summary>The all-clear: sent on a channel only if the action was reported there before (master plan 8.4).</summary>
    public static bool IsAllClear(string kind) => kind is ActionLevelLowered or ActionWithdrawn;
}

internal static class Channels
{
    public const string InApp = "in_app";
    public const string Email = "email";
    public const string Webhook = "webhook";

    /// <summary>Web Push to the members' devices (task 5.5, ADR 041); immediate only.</summary>
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

    /// <summary>Which channels a kind of recipient can be reached on.</summary>
    public static bool Reaches(string kind, string channel) => kind switch
    {
        Role or User => channel is Channels.InApp or Channels.Email or Channels.Push,
        Address => channel == Channels.Email,
        Endpoint => channel == Channels.Webhook,
        _ => false,
    };
}

/// <summary>
/// <c>recipient_key</c> (master plan 4.7): the user, the confirmed external address or the webhook endpoint,
/// with its kind in front so that ids of different kinds never collide.
/// </summary>
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
