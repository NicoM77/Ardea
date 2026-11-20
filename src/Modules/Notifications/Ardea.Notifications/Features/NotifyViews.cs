namespace Ardea.Notifications.Features;

/// <summary>A notification rule (master plan 8.3).</summary>
/// <param name="Id">The rule.</param>
/// <param name="Name">Its name.</param>
/// <param name="Enabled">Whether it is evaluated.</param>
/// <param name="ProjectIds">The projects it applies to; empty means all.</param>
/// <param name="MinLevel">The lowest level it reports, 2 to 5; warnings have no level.</param>
/// <param name="Events">Events of master plan 8.2.</param>
/// <param name="Preset">The default rule it started as: <c>critical</c>, <c>high</c> or <c>weekly</c>.</param>
/// <param name="Targets">Who hears about it, how and when.</param>
/// <param name="Warnings">What keeps it from working fully, e.g. <c>email_unavailable</c> without SMTP.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="ChangedAt">When it last changed.</param>
internal sealed record NotificationRuleView(
    Guid Id, string Name, bool Enabled, IReadOnlyList<Guid> ProjectIds, int MinLevel, IReadOnlyList<string> Events, string? Preset,
    IReadOnlyList<NotificationTargetView> Targets, IReadOnlyList<string> Warnings, DateTimeOffset CreatedAt, DateTimeOffset ChangedAt);

/// <param name="Id">The target.</param>
/// <param name="Channel"><c>in_app</c>, <c>email</c>, <c>webhook</c> or <c>push</c> (immediate only).</param>
/// <param name="RecipientKind"><c>role</c> (that role and the ones above), <c>user</c>, <c>address</c> or <c>endpoint</c>.</param>
/// <param name="RecipientRef">The role, user id, external address id or endpoint id.</param>
/// <param name="RecipientLabel">The member's name, the address or the endpoint's name; empty when it no longer exists.</param>
/// <param name="Mode"><c>immediate</c> or <c>digest</c>.</param>
/// <param name="DigestCron">Five-field cron in the organisation's time zone, for digests.</param>
/// <param name="SendEmpty">A digest goes out even when nothing happened.</param>
/// <param name="NextDigestAt">When the next digest is due.</param>
internal sealed record NotificationTargetView(
    Guid Id, string Channel, string RecipientKind, string RecipientRef, string? RecipientLabel, string Mode, string? DigestCron, bool SendEmpty, DateTimeOffset? NextDigestAt);

/// <summary>Creates or replaces a rule with its targets.</summary>
internal sealed record NotificationRuleRequest(
    string? Name, bool? Enabled, IReadOnlyList<Guid>? ProjectIds, int? MinLevel, IReadOnlyList<string>? Events, IReadOnlyList<NotificationTargetRequest>? Targets);

internal sealed record NotificationTargetRequest(string? Channel, string? RecipientKind, string? RecipientRef, string? Mode, string? DigestCron, bool? SendEmpty);

/// <summary>A channel and whether it can be used now (master plan 8.5: without SMTP, e-mail is off).</summary>
/// <param name="Channel"><c>in_app</c>, <c>email</c>, <c>webhook</c> or <c>push</c>.</param>
/// <param name="Available">Whether rules can reach anyone through it.</param>
/// <param name="Reason"><c>smtp_not_configured</c>; empty when available.</param>
internal sealed record NotificationChannelView(string Channel, bool Available, string? Reason);

/// <summary>A webhook target. The address is shown by its host only; the secret only once, right after creating or rotating it.</summary>
internal sealed record WebhookEndpointView(
    Guid Id, string Name, string Host, bool Enabled, string? LastStatus, DateTimeOffset? LastStatusAt, DateTimeOffset CreatedAt, string? Secret = null);

/// <summary>Creates or changes a webhook target; on change, a field left out stays as it is.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Url">The http or https address the messages are posted to.</param>
/// <param name="Enabled">Whether it receives messages.</param>
/// <param name="RotateSecret">Replace the shared secret; the answer carries the new one.</param>
internal sealed record WebhookEndpointRequest(string? Name, string? Url, bool? Enabled, bool? RotateSecret);

/// <summary>The outcome of a test message.</summary>
internal sealed record WebhookTestView(bool Ok, string? Status, string? Error);

/// <summary>An e-mail address outside the organisation.</summary>
/// <param name="Id">The address entry.</param>
/// <param name="Address">The address.</param>
/// <param name="State"><c>pending</c> (waits for confirmation), <c>confirmed</c>, <c>unsubscribed</c> or <c>expired</c>.</param>
/// <param name="ConfirmedAt">When the recipient confirmed it.</param>
/// <param name="CreatedAt">When it was added.</param>
internal sealed record ExternalAddressView(Guid Id, string Address, string State, DateTimeOffset? ConfirmedAt, DateTimeOffset CreatedAt);

internal sealed record ExternalAddressRequest(string? Address);

/// <summary>The token of a link in a mail to an external address: a confirmation or the signed unsubscribe token.</summary>
internal sealed record AddressLinkRequest(string? Token);

/// <summary>What the signed link of an external address shows before the recipient acts.</summary>
internal sealed record AddressSubscriptionView(string OrganizationName, string Address, string State);

/// <summary>One message of the delivery log (master plan 8.6).</summary>
/// <param name="Id">The delivery.</param>
/// <param name="Kind"><c>immediate</c>, <c>digest</c>, or <c>reminder</c> for a run of the reminders by level (ADR 048).</param>
/// <param name="Channel">The channel.</param>
/// <param name="Recipient">Who it went to.</param>
/// <param name="State"><c>pending</c>, <c>sending</c>, <c>sent</c>, <c>failed</c> (retrying), <c>dead</c> or <c>suppressed</c>.</param>
/// <param name="Attempts">Attempts so far.</param>
/// <param name="LastError">Why the last attempt failed or why it was suppressed.</param>
/// <param name="Events">For immediate messages: how many events it covers; for a reminder run: how many reminders it names.</param>
/// <param name="SendAfter">When it goes out at the earliest.</param>
/// <param name="SentAt">When it went out.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="PeriodStart">For digests: the start of the period; for a reminder run, the previous run.</param>
/// <param name="PeriodEnd">For digests: the end of the period; for a reminder run, the run.</param>
internal sealed record DeliveryView(
    Guid Id, string Kind, string Channel, DeliveryRecipientView Recipient, string State, int Attempts, string? LastError, int Events,
    DateTimeOffset SendAfter, DateTimeOffset? SentAt, DateTimeOffset CreatedAt, DateTimeOffset? PeriodStart, DateTimeOffset? PeriodEnd);

/// <param name="Kind"><c>user</c>, <c>address</c> or <c>endpoint</c>.</param>
/// <param name="Id">The member, address entry or endpoint.</param>
/// <param name="Label">The member's name, the address or the endpoint's name; empty when it no longer exists.</param>
internal sealed record DeliveryRecipientView(string Kind, Guid Id, string Label);

internal sealed record DeliveryListView(IReadOnlyList<DeliveryView> Items, string? NextCursor);

/// <summary>An in-app notification of the signed-in member (master plan 8.5).</summary>
/// <param name="Id">The notification.</param>
/// <param name="Kind">The event of master plan 8.2, or <c>delivery.failed</c>.</param>
/// <param name="Level">The level it concerns; empty for warnings.</param>
/// <param name="Title">«[5 Critical] #143 Puppet Enterprise in Platform Ops».</param>
/// <param name="Body">What happened, in one line.</param>
/// <param name="Link">The page in the web app, e.g. <c>/o/acme/actions/143</c>.</param>
/// <param name="CreatedAt">When it arrived.</param>
/// <param name="ReadAt">When the member read it; empty while unread.</param>
internal sealed record InAppNotificationView(Guid Id, string Kind, int? Level, string Title, string Body, string? Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

internal sealed record InAppNotificationListView(IReadOnlyList<InAppNotificationView> Items, string? NextCursor, int Unread);

internal sealed record UnreadCountView(int Unread);

/// <summary>Marks notifications read: the given ones, or all with <c>all</c>.</summary>
internal sealed record MarkNotificationsReadRequest(IReadOnlyList<Guid>? Ids, bool? All);

/// <summary>The member's own notification settings in this organisation (master plan 8.3).</summary>
/// <param name="MutedChannels">Channels the member muted.</param>
/// <param name="Locale">The language of messages; empty follows the organisation.</param>
/// <param name="CanMuteCritical">Whether muting holds for level 5 too; the organisation decides.</param>
internal sealed record NotificationPreferencesView(IReadOnlyList<string> MutedChannels, string? Locale, bool CanMuteCritical);

internal sealed record NotificationPreferencesRequest(IReadOnlyList<string>? MutedChannels, string? Locale);
