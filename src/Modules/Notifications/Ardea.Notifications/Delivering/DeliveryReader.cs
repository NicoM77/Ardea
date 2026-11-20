using Ardea.Assessment.Contracts;
using Ardea.Iam.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Notifications.Evaluation;
using Ardea.Notifications.Messages;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Secrets;
using Ardea.Platform.Settings;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Ardea.Notifications.Delivering;

/// <summary>What the first, short step of a delivery found.</summary>
/// <param name="Message">The message to send, or <c>null</c>.</param>
/// <param name="Suppress">Why nothing is sent, when the recipient is gone or the channel is off.</param>
internal sealed record DeliveryStart(Outgoing? Message, string? Suppress);

/// <summary>
/// The first step of a delivery (master plan 9.2, «job with outside contact»): marks the message as being sent,
/// so no further event joins it, and reads everything the channel needs. Runs in a short unit of work of its
/// own; the call to the outside happens after it has committed.
/// </summary>
internal sealed class DeliveryReader(
    NotifyDbContext db,
    IActionFacts facts,
    IEntryQueries entries,
    IOrganizationProfiles organizations,
    IOrganizationMembers members,
    IInstanceSettings settings,
    ISecretProtector secrets,
    ITenantContext tenant,
    IConfiguration configuration)
{
    public const string UrlPurpose = "notify.webhook-url";
    public const string SecretPurpose = "notify.webhook-secret";
    public const string UnsubscribePurpose = "notify.unsubscribe";

    /// <summary><c>null</c> when there is nothing to send: unknown, already sent, given up or suppressed.</summary>
    public async Task<DeliveryStart?> StartAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        var organization = tenant.RequiredOrganizationId;
        // Takes the row lock: an evaluation that holds the waiting message finishes adding to it first (Evaluator.WaitingAsync).
        await db.Database.ExecuteSqlAsync(
            $"UPDATE notify.delivery SET state = 'sending' WHERE id = {deliveryId} AND organization_id = {organization} AND state = 'pending'", cancellationToken);
        var delivery = await db.Deliveries.AsNoTracking().SingleOrDefaultAsync(d => d.Id == deliveryId, cancellationToken);
        if (delivery is null || delivery.State is not ("sending" or "failed"))
        {
            return null;
        }

        var profile = await organizations.FindAsync(organization, cancellationToken);
        if (profile is null)
        {
            return new DeliveryStart(null, "The organisation no longer exists.");
        }

        var directory = await RecipientDirectory.LoadAsync(db, members, cancellationToken);
        var (kind, id) = RecipientKeys.Parse(delivery.RecipientKey);
        var publicUrl = Evaluator.PublicUrl(configuration);
        Recipient? recipient = kind switch
        {
            RecipientKinds.User => directory.User(id),
            RecipientKinds.Address => directory.Resolve(new RuleTarget { Channel = Channels.Email, RecipientKind = kind, RecipientRef = id.ToString(), Mode = "immediate" }).FirstOrDefault(),
            RecipientKinds.Endpoint => directory.Resolve(new RuleTarget { Channel = Channels.Webhook, RecipientKind = kind, RecipientRef = id.ToString(), Mode = "immediate" }).FirstOrDefault(),
            _ => null,
        };
        if (recipient is null)
        {
            return new DeliveryStart(null, "The recipient is no longer reachable: left the organisation, unsubscribed, or the endpoint is off.");
        }

        var items = delivery.Kind switch
        {
            "digest" => await DigestItemsAsync(delivery, cancellationToken),
            "reminder" => await ReminderItemsAsync(delivery, cancellationToken),
            _ => await ImmediateItemsAsync(delivery, cancellationToken),
        };
        var content = new MessageContent
        {
            DeliveryId = delivery.Id,
            Kind = delivery.Kind,
            Organization = profile,
            Locale = directory.Locale(recipient, profile.Locale),
            PublicUrl = publicUrl,
            Items = items,
            PeriodStart = delivery.PeriodStart,
            PeriodEnd = delivery.PeriodEnd,
        };
        if (delivery.Kind == "immediate" && items.Count == 0)
        {
            return new DeliveryStart(null, "None of its events exists any more.");
        }

        if (delivery.Kind == "reminder" && items.Count == 0)
        {
            return new DeliveryStart(null, "None of its reminders is open any more.");
        }

        var message = new Outgoing { Content = content, Recipient = recipient, Channel = delivery.Channel };
        switch (recipient.Kind)
        {
            case RecipientKinds.User when delivery.Channel == Channels.Push:
                // Through the worker's policy: the subscriptions of this member of the job's organisation (ADR 041).
                var devices = await db.PushSubscriptions.AsNoTracking().Where(p => p.UserId == recipient.Id).OrderBy(p => p.CreatedAt)
                    .Select(p => new PushDevice(p.Id, p.Endpoint, p.P256dh, p.Auth)).ToListAsync(cancellationToken);
                if (devices.Count == 0)
                {
                    return new DeliveryStart(null, "The member has no device subscribed to push any more.");
                }

                message = message with { PushDevices = devices };
                break;
            case RecipientKinds.User:
                message = message with { Unsubscribe = new Uri(publicUrl, $"o/{profile.Slug}/settings/notifications") };
                break;
            case RecipientKinds.Address:
                var token = Uri.EscapeDataString(UnsubscribeToken(secrets, organization, recipient.Id));
                message = message with
                {
                    Unsubscribe = new Uri(publicUrl, $"notifications/unsubscribe#token={token}"),
                    OneClick = new Uri(publicUrl, $"api/v1/notify/addresses/unsubscribe/one-click?token={token}"),
                };
                break;
            case RecipientKinds.Endpoint:
                var endpoint = await db.Endpoints.AsNoTracking().SingleAsync(e => e.Id == recipient.Id, cancellationToken);
                var url = secrets.Unprotect(UrlPurpose, endpoint.UrlEnc) is { } text && Uri.TryCreate(text, UriKind.Absolute, out var parsed) ? parsed : null;
                var released = InstanceSettingDefinitions.Hosts((await settings.GetAsync(InstanceSettingDefinitions.WebhookInternalHosts, cancellationToken)).Value);
                message = message with
                {
                    WebhookUrl = url,
                    WebhookSecret = secrets.Unprotect(SecretPurpose, endpoint.SecretEnc),
                    AllowInternal = url is not null && WebhookChannel.IsReleased(url, released),
                };
                break;
        }

        return new DeliveryStart(message, null);
    }

    /// <summary>The signed token of an external address's unsubscribe link; it works without signing in and does not expire.</summary>
    public static string UnsubscribeToken(ISecretProtector secrets, Guid organization, Guid address) =>
        secrets.Protect(UnsubscribePurpose, $"{organization:N}:{address:N}");

    public static (Guid Organization, Guid Address)? ReadUnsubscribeToken(ISecretProtector secrets, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || secrets.Unprotect(UnsubscribePurpose, token.Trim()) is not { } plain)
        {
            return null;
        }

        var parts = plain.Split(':');
        return parts.Length == 2 && Guid.TryParse(parts[0], out var organization) && Guid.TryParse(parts[1], out var address) ? (organization, address) : null;
    }

    private async Task<IReadOnlyList<MessageItem>> ImmediateItemsAsync(Delivery delivery, CancellationToken cancellationToken)
    {
        var covered = await db.DeliveryEvents.AsNoTracking().Where(e => e.DeliveryId == delivery.Id).OrderBy(e => e.OccurredAt).ToListAsync(cancellationToken);
        var items = await ItemsAsync(covered.Select(e => (e.EventId, e.Event, e.OccurredAt, e.EntryId, e.Level)).ToList(), cancellationToken);
        // A notice answered or withdrawn while the message waited asks nobody any more (ADR 048).
        return [.. items.Where(i => i.Notice is not { State: not "open" })];
    }

    /// <summary>
    /// A run of the reminders by level (ADR 048): the reminders it named that are still open now, at the level they stand
    /// at now; one done in the meantime is left out.
    /// </summary>
    private async Task<IReadOnlyList<MessageItem>> ReminderItemsAsync(Delivery delivery, CancellationToken cancellationToken)
    {
        var rows = await db.DeliveryEvents.AsNoTracking().Where(e => e.DeliveryId == delivery.Id && e.RemediationId != null).ToListAsync(cancellationToken);
        var actions = (await facts.ActionsAsync([.. rows.Select(r => r.RemediationId!.Value).Distinct()], cancellationToken)).ToDictionary(a => a.Id);
        return [.. rows
            .Select(r => actions.GetValueOrDefault(r.RemediationId!.Value) is { Status: not "closed" } action
                ? new MessageItem { EventId = r.EventId, Event = NotifyEvents.ReminderDue, At = r.OccurredAt, Action = action, Level = action.Level }
                : null)
            .OfType<MessageItem>()
            .OrderByDescending(i => i.Level).ThenBy(i => i.At)];
    }

    /// <summary>
    /// A digest holds what its target noted for the period, the actions that grew in it and matched the rule,
    /// and marks what this recipient already heard about at once on the same channel (master plan 8.4).
    /// </summary>
    private async Task<IReadOnlyList<MessageItem>> DigestItemsAsync(Delivery delivery, CancellationToken cancellationToken)
    {
        var noted = await db.DigestItems.AsNoTracking()
            .Where(i => i.RuleTargetId == delivery.RuleTargetId && i.PeriodEnd == delivery.PeriodEnd)
            .OrderBy(i => i.OccurredAt).ToListAsync(cancellationToken);
        var items = (await ItemsAsync(noted.Select(i => (i.EventId, i.Event, i.OccurredAt, i.EntryId, i.Level)).ToList(), cancellationToken)).ToList();

        var eventIds = items.Select(i => i.EventId).ToArray();
        var reported = (await db.DeliveryEvents.AsNoTracking()
                .Where(e => e.RecipientKey == delivery.RecipientKey && e.Channel == delivery.Channel && eventIds.Contains(e.EventId))
                .Join(db.Deliveries.AsNoTracking().Where(d => d.Kind == "immediate" && (d.State == "sent" || d.State == "pending" || d.State == "sending")),
                    e => e.DeliveryId, d => d.Id, (e, d) => e.EventId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        items = [.. items.Select(i => reported.Contains(i.EventId) ? i with { AlreadyReported = true } : i)];

        var rule = await db.Targets.AsNoTracking().Where(t => t.Id == delivery.RuleTargetId)
            .Join(db.Rules.AsNoTracking(), t => t.RuleId, r => r.Id, (t, r) => r).SingleOrDefaultAsync(cancellationToken);
        if (rule is not null && delivery.PeriodStart is { } start && delivery.PeriodEnd is { } end)
        {
            var growth = await facts.FindingsJoinedAsync(start, end, cancellationToken);
            var listed = items.Select(i => i.Action?.Id).OfType<Guid>().ToHashSet();
            var grown = (await facts.ActionsAsync([.. growth.Select(g => g.RemediationId).Where(id => !listed.Contains(id))], cancellationToken))
                .Where(a => a.Status != "closed" && a.Level >= rule.MinLevel && (rule.ProjectIds.Length == 0 || rule.ProjectIds.Contains(a.ProjectId)))
                .ToList();
            items.AddRange(grown.Select(a => new MessageItem
            {
                EventId = a.Id,
                Event = "finding.joined",
                At = end,
                Action = a,
                Level = a.Level,
                NewFindings = growth.First(g => g.RemediationId == a.Id).Joined,
            }));
        }

        return items;
    }

    private async Task<IReadOnlyList<MessageItem>> ItemsAsync(
        IReadOnlyList<(Guid EventId, string Event, DateTimeOffset At, Guid? EntryId, int? Level)> rows, CancellationToken cancellationToken)
    {
        var events = (await facts.EventsAsync([.. rows.Where(r => r.EntryId is null).Select(r => r.EventId)], cancellationToken)).ToDictionary(e => e.Id);
        var actions = (await facts.ActionsAsync([.. events.Values.Select(e => e.RemediationId).OfType<Guid>().Distinct()], cancellationToken)).ToDictionary(a => a.Id);
        var projects = await facts.ProjectNamesAsync([.. events.Values.Select(e => e.ProjectId).OfType<Guid>().Distinct()], cancellationToken);
        var notices = (await facts.NoticesAsync([.. events.Values.Select(e => e.NoticeId).OfType<Guid>().Distinct()], cancellationToken)).ToDictionary(n => n.Id);
        var items = new List<MessageItem>();
        foreach (var row in rows)
        {
            if (row.EntryId is { } entryId)
            {
                if (await entries.FindAsync(entryId, cancellationToken) is { } entry)
                {
                    items.Add(new MessageItem
                    {
                        EventId = row.EventId,
                        Event = row.Event,
                        At = row.At,
                        Warning = new WarningFacts(entry.Id, entry.Key, entry.Title, entry.PublishedAt),
                    });
                }

                continue;
            }

            if (!events.TryGetValue(row.EventId, out var fact))
            {
                continue;
            }

            var item = new MessageItem { EventId = fact.Id, Event = fact.Kind, At = fact.At, Data = fact.Data, Level = row.Level };
            if (NotifyEvents.IsNotice(fact.Kind))
            {
                if (MessageItem.ForNotice(fact, notices, projects) is { } notice)
                {
                    items.Add(notice);
                }
            }
            else if (fact.RemediationId is { } remediation && actions.TryGetValue(remediation, out var action))
            {
                items.Add(item with { Action = action, Level = row.Level ?? action.Level });
            }
            else if (fact.ProjectId is { } project)
            {
                items.Add(item with
                {
                    Backfill = new BackfillFacts(project, projects.GetValueOrDefault(project, string.Empty), item.DataInt("actions") ?? 0, item.DataInt("highestLevel"), item.DataInt("findings") ?? 0),
                });
            }
        }

        return items;
    }
}
