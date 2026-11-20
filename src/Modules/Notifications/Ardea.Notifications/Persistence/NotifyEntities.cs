using Ardea.Platform.Persistence;

namespace Ardea.Notifications.Persistence;

internal sealed class NotificationRule : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Name { get; set; }

    public bool Enabled { get; set; } = true;

    public Guid[] ProjectIds { get; set; } = [];

    public int MinLevel { get; set; }

    public string[] Events { get; set; } = [];

    public string? Preset { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public List<RuleTarget> Targets { get; set; } = [];
}

internal sealed class RuleTarget : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid RuleId { get; set; }

    public required string Channel { get; set; }

    public required string RecipientKind { get; set; }

    public required string RecipientRef { get; set; }

    public required string Mode { get; set; }

    public string? DigestCron { get; set; }

    public bool SendEmpty { get; set; }

    public DateTimeOffset? NextDigestAt { get; set; }

    public DateTimeOffset? LastDigestAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class WebhookEndpoint : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Name { get; set; }

    public required string UrlEnc { get; set; }

    public required string SecretEnc { get; set; }

    public bool Enabled { get; set; } = true;

    public string? LastStatus { get; set; }

    public DateTimeOffset? LastStatusAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class ExternalAddress : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Address { get; set; }

    public required string NormalizedAddress { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    public string? TokenHash { get; set; }

    public DateTimeOffset? TokenExpiresAt { get; set; }

    public DateTimeOffset? UnsubscribedAt { get; set; }

    public DateTimeOffset? ConfirmationSentAt { get; set; }

    public int ConfirmationsSent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class RecipientState : IOrganizationOwned
{
    public Guid OrganizationId { get; set; }

    public Guid RemediationId { get; set; }

    public required string RecipientKey { get; set; }

    public required string Channel { get; set; }

    public int LastLevel { get; set; }

    public string LastExploitation { get; set; } = "none";

    public bool FixNotified { get; set; }

    public DateTimeOffset? LastNotifiedAt { get; set; }
}

internal sealed class Delivery : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Kind { get; set; }

    public required string Channel { get; set; }

    public required string RecipientKey { get; set; }

    public DateTimeOffset SendAfter { get; set; }

    public required string State { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? RuleTargetId { get; set; }

    public DateTimeOffset? PeriodStart { get; set; }

    public DateTimeOffset? PeriodEnd { get; set; }
}

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

    public Guid? NoticeId { get; set; }

    public int? Level { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}

internal sealed class ReminderSchedule : IOrganizationOwned
{
    public Guid OrganizationId { get; set; }

    public string Level5 { get; set; } = Delivering.ReminderRhythms.Daily;

    public string Level4 { get; set; } = Delivering.ReminderRhythms.Weekly;

    public string Level3 { get; set; } = Delivering.ReminderRhythms.Monthly;

    public DateTimeOffset? NextRunAt { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

internal sealed class ReminderState : IOrganizationOwned
{
    public Guid OrganizationId { get; set; }

    public Guid RemediationId { get; set; }

    public DateTimeOffset LastRemindedAt { get; set; }
}

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

    public DateTimeOffset? PeriodEnd { get; set; }
}

internal sealed class Notification : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid EventId { get; set; }

    public Guid? RemediationId { get; set; }

    public Guid? EntryId { get; set; }

    public Guid? ProjectId { get; set; }

    public required string Kind { get; set; }

    public int? Level { get; set; }

    public required string Title { get; set; }

    public required string Body { get; set; }

    public string? Link { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}

internal sealed class UserPreference : IOrganizationOwned
{
    public Guid UserId { get; set; }

    public Guid OrganizationId { get; set; }

    public string[] MutedChannels { get; set; } = [];

    public string? Locale { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

internal sealed class PendingEvent : IOrganizationOwned
{
    public Guid EventId { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Kind { get; set; }

    public DateTimeOffset At { get; set; }

    public Guid? EntryId { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? FailedAt { get; set; }
}

internal sealed class PushSubscription
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string Endpoint { get; set; }

    public required string EndpointHash { get; set; }

    public required string P256dh { get; set; }

    public required string Auth { get; set; }

    public string? UserAgent { get; set; }

    public required string Label { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    public int FailedCount { get; set; }

    public int RejectedCount { get; set; }
}
