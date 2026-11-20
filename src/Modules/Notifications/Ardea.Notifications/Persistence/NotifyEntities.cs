using Ardea.Platform.Persistence;

namespace Ardea.Notifications.Persistence;

/// <summary>A notification rule (master plan 4.7, 8.3): where it applies, from which level, for which events.</summary>
internal sealed class NotificationRule : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Name { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Empty means every project.</summary>
    public Guid[] ProjectIds { get; set; } = [];

    /// <summary>2 to 5; warnings have no level and match every rule that names them.</summary>
    public int MinLevel { get; set; }

    /// <summary>Kinds of master plan 8.2.</summary>
    public string[] Events { get; set; } = [];

    /// <summary>The default rule it was created as (<c>critical</c>, <c>high</c>, <c>weekly</c>); <c>null</c> for own rules.</summary>
    public string? Preset { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public List<RuleTarget> Targets { get; set; } = [];
}

/// <summary>Who hears about a rule's events, over which channel, and when (master plan 4.7, 8.3).</summary>
internal sealed class RuleTarget : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid RuleId { get; set; }

    /// <summary><c>in_app</c>, <c>email</c>, <c>webhook</c> or <c>push</c>.</summary>
    public required string Channel { get; set; }

    /// <summary><c>role</c>, <c>user</c>, <c>address</c> or <c>endpoint</c>.</summary>
    public required string RecipientKind { get; set; }

    /// <summary>The role (at least it), the user id, the external address id or the webhook endpoint id.</summary>
    public required string RecipientRef { get; set; }

    /// <summary><c>immediate</c> or <c>digest</c>.</summary>
    public required string Mode { get; set; }

    /// <summary>Five fields, in the organisation's time zone; only for digests.</summary>
    public string? DigestCron { get; set; }

    /// <summary>A digest goes out even when nothing happened: «checked, nothing to do».</summary>
    public bool SendEmpty { get; set; }

    /// <summary>When the next digest is due; <c>null</c> until it is first computed.</summary>
    public DateTimeOffset? NextDigestAt { get; set; }

    /// <summary>The end of the last digest's period: the next one starts there.</summary>
    public DateTimeOffset? LastDigestAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A webhook target (master plan 4.7, 8.5); address and secret are encrypted (master plan 9.4).</summary>
internal sealed class WebhookEndpoint : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Name { get; set; }

    public required string UrlEnc { get; set; }

    public required string SecretEnc { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>The outcome of the last call: <c>200</c>, <c>timeout</c>, <c>refused</c>, …</summary>
    public string? LastStatus { get; set; }

    public DateTimeOffset? LastStatusAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An e-mail address outside the organisation; it receives messages only once confirmed (master plan 8.3).</summary>
internal sealed class ExternalAddress : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Address { get; set; }

    public required string NormalizedAddress { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>SHA-256 of the confirmation token; the link carries the token.</summary>
    public string? TokenHash { get; set; }

    public DateTimeOffset? TokenExpiresAt { get; set; }

    /// <summary>The recipient unsubscribed with the signed link; no message goes there any more, nor a new confirmation.</summary>
    public DateTimeOffset? UnsubscribedAt { get; set; }

    /// <summary>When a confirmation mail was last asked for: the cooldown and the daily limits count from it.</summary>
    public DateTimeOffset? ConfirmationSentAt { get; set; }

    /// <summary>Confirmation mails asked for so far; at most <c>AddressLimits.ConfirmationsPerAddress</c>.</summary>
    public int ConfirmationsSent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>What a recipient has learnt about an action on a channel (master plan 4.7, 8.4).</summary>
internal sealed class RecipientState : IOrganizationOwned
{
    public Guid OrganizationId { get; set; }

    public Guid RemediationId { get; set; }

    public required string RecipientKey { get; set; }

    public required string Channel { get; set; }

    public int LastLevel { get; set; }

    public string LastExploitation { get; set; } = "none";

    public bool FixNotified { get; set; }

    /// <summary>When a message last went out; <c>null</c> while the state was only set (first fill) or lowered.</summary>
    public DateTimeOffset? LastNotifiedAt { get; set; }
}

/// <summary>One message to one recipient on one channel, and the delivery log at the same time (master plan 4.7, 8.6).</summary>
internal sealed class Delivery : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    /// <summary><c>immediate</c>, <c>digest</c>, or <c>reminder</c> for a run of the reminders by level (ADR 048).</summary>
    public required string Kind { get; set; }

    public required string Channel { get; set; }

    public required string RecipientKey { get; set; }

    public DateTimeOffset SendAfter { get; set; }

    /// <summary><c>pending</c>, <c>sending</c>, <c>sent</c>, <c>failed</c> (retrying), <c>dead</c> or <c>suppressed</c>.</summary>
    public required string State { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>For digests: the target, and the period the message covers.</summary>
    public Guid? RuleTargetId { get; set; }

    public DateTimeOffset? PeriodStart { get; set; }

    public DateTimeOffset? PeriodEnd { get; set; }
}

/// <summary>An event an immediate message covers; recipient, channel and event are unique together (master plan 8.4).</summary>
internal sealed class DeliveryEvent : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid DeliveryId { get; set; }

    public required string RecipientKey { get; set; }

    public required string Channel { get; set; }

    public Guid EventId { get; set; }

    public required string Event { get; set; }

    public Guid? RemediationId { get; set; }

    public Guid? EntryId { get; set; }

    public Guid? ProjectId { get; set; }

    /// <summary>The notice of <c>notice.raised</c> or <c>notice.reappeared</c>: announced once per recipient and channel (ADR 048).</summary>
    public Guid? NoticeId { get; set; }

    /// <summary>The level the event was evaluated at; <c>null</c> for warnings and notices.</summary>
    public int? Level { get; set; }

    /// <summary>When it happened; for a line of the reminders by level, since when the reminder stands at its level.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// How often the organisation's open reminders remind, by level (master plan 8.7, 17.5; ADR 048), and when the next run
/// is. One row per organisation, written by its first run with the defaults or by the settings.
/// </summary>
internal sealed class ReminderSchedule : IOrganizationOwned
{
    public Guid OrganizationId { get; set; }

    /// <summary><c>daily</c>, <c>weekly</c>, <c>monthly</c> or <c>off</c>.</summary>
    public string Level5 { get; set; } = Delivering.ReminderRhythms.Daily;

    public string Level4 { get; set; } = Delivering.ReminderRhythms.Weekly;

    public string Level3 { get; set; } = Delivering.ReminderRhythms.Monthly;

    /// <summary>The next run, weekdays at 08:00 in the organisation's time zone; <c>null</c> until the first run schedules it.</summary>
    public DateTimeOffset? NextRunAt { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>When an open reminder last reminded its recipients (master plan 8.7, ADR 048); the rhythm counts from it.</summary>
internal sealed class ReminderState : IOrganizationOwned
{
    public Guid OrganizationId { get; set; }

    public Guid RemediationId { get; set; }

    public DateTimeOffset LastRemindedAt { get; set; }

    /// <summary>The level it was reminded at.</summary>
    public int Level { get; set; }
}

/// <summary>An event noted for a digest target (master plan 4.7, 8.4); target and event are unique together.</summary>
internal sealed class DigestItem : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid RuleTargetId { get; set; }

    public Guid EventId { get; set; }

    public required string Event { get; set; }

    public Guid? RemediationId { get; set; }

    public Guid? EntryId { get; set; }

    public Guid? ProjectId { get; set; }

    public int? Level { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>The end of the period whose digest took it; <c>null</c> while it waits.</summary>
    public DateTimeOffset? PeriodEnd { get; set; }
}

/// <summary>An in-app notification of one member (master plan 4.7, 8.5): user data that also carries the organisation.</summary>
internal sealed class Notification : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid EventId { get; set; }

    public Guid? RemediationId { get; set; }

    public Guid? EntryId { get; set; }

    public Guid? ProjectId { get; set; }

    /// <summary>The event of master plan 8.2, or <c>delivery.failed</c> for the owners' warning (8.6).</summary>
    public required string Kind { get; set; }

    public int? Level { get; set; }

    public required string Title { get; set; }

    public required string Body { get; set; }

    /// <summary>The page in the web app, relative to its root, e.g. <c>/o/acme/actions/143</c>.</summary>
    public string? Link { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>A member's own settings in one organisation (master plan 4.7, 8.3): muted channels and language.</summary>
internal sealed class UserPreference : IOrganizationOwned
{
    public Guid UserId { get; set; }

    public Guid OrganizationId { get; set; }

    public string[] MutedChannels { get; set; } = [];

    /// <summary>The language of messages to this member; <c>null</c> follows the organisation.</summary>
    public string? Locale { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>
/// An event waiting for the rules (ADR 038): written in the transaction of the event itself, taken by the
/// organisation's evaluation job in order. Warnings without identifier enter here per organisation.
/// </summary>
internal sealed class PendingEvent : IOrganizationOwned
{
    public Guid EventId { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Kind { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary>The entry of a warning without identifier.</summary>
    public Guid? EntryId { get; set; }

    /// <summary>Evaluations of this event that failed; the others of its organisation went on (ADR 038).</summary>
    public int Attempts { get; set; }

    public string? LastError { get; set; }

    /// <summary>Given up after <c>EvaluateEvents.MaxEventAttempts</c>; stays for the instance status and the operator.</summary>
    public DateTimeOffset? FailedAt { get; set; }
}

/// <summary>
/// A device's Web Push subscription (master plan 4.7, 8.5): user data without an organisation. The member
/// writes their own; the worker reads, counts and removes those of a named recipient who is a member of the
/// job's organisation.
/// </summary>
internal sealed class PushSubscription
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The push service's address for this device: a capability, never logged.</summary>
    public required string Endpoint { get; set; }

    /// <summary>Lower-case hex SHA-256 of the endpoint, unique per user.</summary>
    public required string EndpointHash { get; set; }

    /// <summary>The browser's P-256 key, base64url.</summary>
    public required string P256dh { get; set; }

    /// <summary>The browser's 16-byte authentication secret, base64url.</summary>
    public required string Auth { get; set; }

    /// <summary>The browser's User-Agent when it subscribed.</summary>
    public string? UserAgent { get; set; }

    /// <summary>«Chrome on macOS», derived from the User-Agent.</summary>
    public required string Label { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    /// <summary>Failed attempts since the last success.</summary>
    public int FailedCount { get; set; }

    /// <summary>Refusals of the instance's key (401, 403) in a row; the third removes the subscription.</summary>
    public int RejectedCount { get; set; }
}
