namespace Ardea.Notifications.Features;

internal sealed record NotificationRuleView(
    Guid Id, string Name, bool Enabled, IReadOnlyList<Guid> ProjectIds, int MinLevel, IReadOnlyList<string> Events, string? Preset,
    IReadOnlyList<NotificationTargetView> Targets, IReadOnlyList<string> Warnings, DateTimeOffset CreatedAt, DateTimeOffset ChangedAt);

internal sealed record NotificationTargetView(
    Guid Id, string Channel, string RecipientKind, string RecipientRef, string? RecipientLabel, string Mode, string? DigestCron, bool SendEmpty, DateTimeOffset? NextDigestAt);

internal sealed record NotificationRuleRequest(
    string? Name, bool? Enabled, IReadOnlyList<Guid>? ProjectIds, int? MinLevel, IReadOnlyList<string>? Events, IReadOnlyList<NotificationTargetRequest>? Targets);

internal sealed record NotificationTargetRequest(string? Channel, string? RecipientKind, string? RecipientRef, string? Mode, string? DigestCron, bool? SendEmpty);

internal sealed record NotificationChannelView(string Channel, bool Available, string? Reason);

internal sealed record WebhookEndpointView(
    Guid Id, string Name, string Host, bool Enabled, string? LastStatus, DateTimeOffset? LastStatusAt, DateTimeOffset CreatedAt, string? Secret = null);

internal sealed record WebhookEndpointRequest(string? Name, string? Url, bool? Enabled, bool? RotateSecret);

internal sealed record WebhookTestView(bool Ok, string? Status, string? Error);

internal sealed record ExternalAddressView(Guid Id, string Address, string State, DateTimeOffset? ConfirmedAt, DateTimeOffset CreatedAt);

internal sealed record ExternalAddressRequest(string? Address);

internal sealed record AddressLinkRequest(string? Token);

internal sealed record AddressSubscriptionView(string OrganizationName, string Address, string State);

internal sealed record DeliveryView(
    Guid Id, string Kind, string Channel, DeliveryRecipientView Recipient, string State, int Attempts, string? LastError, int Events,
    DateTimeOffset SendAfter, DateTimeOffset? SentAt, DateTimeOffset CreatedAt, DateTimeOffset? PeriodStart, DateTimeOffset? PeriodEnd);

internal sealed record DeliveryRecipientView(string Kind, Guid Id, string Label);

internal sealed record DeliveryListView(IReadOnlyList<DeliveryView> Items, string? NextCursor);

internal sealed record InAppNotificationView(Guid Id, string Kind, int? Level, string Title, string Body, string? Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

internal sealed record InAppNotificationListView(IReadOnlyList<InAppNotificationView> Items, string? NextCursor, int Unread);

internal sealed record UnreadCountView(int Unread);

internal sealed record MarkNotificationsReadRequest(IReadOnlyList<Guid>? Ids, bool? All);

internal sealed record NotificationPreferencesView(IReadOnlyList<string> MutedChannels, string? Locale, bool CanMuteCritical);

internal sealed record NotificationPreferencesRequest(IReadOnlyList<string>? MutedChannels, string? Locale);
