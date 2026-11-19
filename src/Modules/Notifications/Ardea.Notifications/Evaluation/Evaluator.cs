using Ardea.Assessment.Contracts;
using Ardea.Iam.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Notifications.Delivering;
using Ardea.Notifications.Messages;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Email;
using Ardea.Platform.Jobs;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Ardea.Notifications.Evaluation;

/// <summary>
/// Evaluates the organisation's rules for its events (master plan 8.1 to 8.4): which targets an event
/// reaches, who they are now, whether each recipient learns something new on each channel, and then
/// either an in-app notification at once, a place in the waiting message of that recipient and channel,
/// or a line in a digest. Runs in the organisation's job, under its lock, events in the order they happened.
/// </summary>
internal sealed class Evaluator(
    NotifyDbContext db,
    IActionFacts facts,
    IEntryQueries entries,
    IOrganizationProfiles organizations,
    IOrganizationMembers members,
    IEmailSender email,
    IJobQueue jobs,
    ITenantContext tenant,
    IConfiguration configuration,
    TimeProvider time)
{
    /// <summary>An immediate message waits this long for further events of the same recipient and channel (master plan 8.4).</summary>
    public static readonly TimeSpan Bundling = TimeSpan.FromMinutes(2);

    /// <summary>Immediate messages per channel and organisation and hour; above, the event waits for the digest (master plan 8.6).</summary>
    public const int HourlyLimit = 60;

    /// <summary>Reads what every event of the batch needs once: organisation, rules, recipients, events and actions.</summary>
    public async Task<Batch?> LoadAsync(IReadOnlyList<PendingEvent> pending, CancellationToken cancellationToken)
    {
        var organization = await organizations.FindAsync(tenant.RequiredOrganizationId, cancellationToken);
        if (organization is null)
        {
            return null;
        }

        var rules = await db.Rules.AsNoTracking().Include(r => r.Targets).Where(r => r.Enabled).ToListAsync(cancellationToken);
        var directory = await RecipientDirectory.LoadAsync(db, members, cancellationToken);
        var emailAvailable = await email.IsConfiguredAsync(cancellationToken);
        var events = (await facts.EventsAsync([.. pending.Where(p => p.EntryId is null).Select(p => p.EventId)], cancellationToken)).ToDictionary(e => e.Id);
        var actions = (await facts.ActionsAsync([.. events.Values.Select(e => e.RemediationId).OfType<Guid>().Distinct()], cancellationToken)).ToDictionary(a => a.Id);
        return new Batch(new Context(organization, rules, directory, emailAvailable, PublicUrl(configuration), time.GetUtcNow()), events, actions);
    }

    /// <summary>Evaluates one event of the batch and saves what it decided.</summary>
    public async Task EvaluateAsync(Batch batch, PendingEvent pending, CancellationToken cancellationToken)
    {
        var item = await ItemAsync(pending, batch.Events, batch.Actions, cancellationToken);
        if (item is not null)
        {
            await EvaluateAsync(item, batch.Context, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public static Uri PublicUrl(IConfiguration configuration)
    {
        var url = configuration["PUBLIC_URL"] ?? throw new InvalidOperationException("ARDEA_PUBLIC_URL is not set.");
        return new Uri(url.EndsWith('/') ? url : url + "/");
    }

    private async Task<MessageItem?> ItemAsync(
        PendingEvent pending, Dictionary<Guid, ActionEventFacts> events, Dictionary<Guid, ActionFacts> actions, CancellationToken cancellationToken)
    {
        if (pending.EntryId is { } entryId)
        {
            var entry = await entries.FindAsync(entryId, cancellationToken);
            return entry is null
                ? null
                : new MessageItem
                {
                    EventId = pending.EventId,
                    Event = pending.Kind,
                    At = pending.At,
                    Warning = new WarningFacts(entry.Id, entry.Key, entry.Title, entry.PublishedAt),
                };
        }

        if (!events.TryGetValue(pending.EventId, out var fact))
        {
            return null;
        }

        if (fact.Kind == NotifyEvents.BackfillCompleted && fact.ProjectId is { } project)
        {
            var name = (await facts.ProjectNamesAsync([project], cancellationToken)).GetValueOrDefault(project, string.Empty);
            var data = new MessageItem { EventId = fact.Id, Event = fact.Kind, At = fact.At, Data = fact.Data, Actor = fact.Actor };
            var highest = data.DataInt("highestLevel");
            return data with
            {
                Level = highest,
                Backfill = new BackfillFacts(project, name, data.DataInt("actions") ?? 0, highest, data.DataInt("findings") ?? 0),
            };
        }

        return fact.RemediationId is { } remediation && actions.TryGetValue(remediation, out var action)
            ? new MessageItem { EventId = fact.Id, Event = fact.Kind, At = fact.At, Data = fact.Data, Actor = fact.Actor, Action = action, Level = EventLevel(fact, action) }
            : null;
    }

    /// <summary>
    /// The level the event reports, from its own data: the action may have moved on since, and a rule's threshold and
    /// the state compare against what this event says (raised at 4, then raised to 5, are two events at 4 and 5).
    /// </summary>
    internal static int EventLevel(ActionEventFacts fact, ActionFacts action)
    {
        var item = new MessageItem { EventId = fact.Id, Event = fact.Kind, At = fact.At, Data = fact.Data };
        return fact.Kind switch
        {
            NotifyEvents.ActionRaised => item.DataInt("level"),
            NotifyEvents.ActionLevelRaised or NotifyEvents.ActionLevelLowered => item.DataInt("to"),
            _ => null,
        } ?? action.Level;
    }

    private async Task EvaluateAsync(MessageItem item, Context context, CancellationToken cancellationToken)
    {
        if (item.Event == NotifyEvents.ActionLevelLowered && item.Action is { } lowered)
        {
            // Every recipient's state sinks with the action, whether or not they hear the all-clear (master plan 8.4).
            await LowerStatesAsync(lowered.Id, item.DataInt("to") ?? lowered.Level, cancellationToken);
        }

        var project = item.Action?.ProjectId ?? item.Backfill?.ProjectId;
        // An all-clear is measured against the level it clears.
        var level = item.Event == NotifyEvents.ActionLevelLowered ? Math.Max(item.DataInt("from") ?? 0, item.Level ?? 0) : item.Level ?? (item.Backfill is not null ? 1 : null);
        var rules = context.Rules.Where(r => r.Events.Contains(item.Event)
            && (r.ProjectIds.Length == 0 || (project is { } p && r.ProjectIds.Contains(p)))
            && (level is not { } l || l >= r.MinLevel)).ToList();

        var immediate = new Dictionary<(string Key, string Channel), Recipient>();
        var digestRecipients = new HashSet<(string Key, string Channel)>();
        foreach (var target in rules.SelectMany(r => r.Targets))
        {
            if (!NotifyEvents.AllowedOn(item.Event, target.Channel) || !Usable(target, context))
            {
                continue;
            }

            if (target.Mode == "digest")
            {
                await NoteForDigestAsync(target, item, cancellationToken);
                digestRecipients.UnionWith(context.Directory.Resolve(target).Select(r => (r.Key, target.Channel)));
                continue;
            }

            foreach (var recipient in context.Directory.Resolve(target))
            {
                immediate.TryAdd((recipient.Key, target.Channel), recipient);
            }
        }

        foreach (var (recipient, channel) in Implicit(item, rules.Count > 0, context))
        {
            immediate.TryAdd((recipient.Key, channel), recipient);
        }

        foreach (var ((key, channel), recipient) in immediate)
        {
            if (context.Directory.IsMuted(recipient, channel, item.Level, context.Organization.AllowMuteCritical))
            {
                continue;
            }

            var state = HasState(item.Event) && item.Action is { } action
                ? await db.States.FindAsync([context.Organization.Id, action.Id, key, channel], cancellationToken)
                : null;
            if (!ShouldSend(item, state, channel)
                || await db.DeliveryEvents.AnyAsync(e => e.RecipientKey == key && e.Channel == channel && e.EventId == item.EventId, cancellationToken))
            {
                continue;
            }

            await SendAsync(recipient, channel, item, context, cancellationToken);
            if (HasState(item.Event) && item.Action is { } reported)
            {
                Record(state, reported, item.Level ?? reported.Level, key, channel, item.Event, context.Now);
            }
        }

        if (item.Backfill is { } backfill)
        {
            // The first fill's actions count as reported to everyone who hears about it, so that only a later rise sends (master plan 8.4).
            var reached = immediate.Keys.Concat(digestRecipients).ToHashSet();
            foreach (var action in await facts.BackfillActionsAsync(backfill.ProjectId, cancellationToken))
            {
                foreach (var (key, channel) in reached)
                {
                    var state = await db.States.FindAsync([context.Organization.Id, action.Id, key, channel], cancellationToken);
                    if (state is null)
                    {
                        db.States.Add(new RecipientState
                        {
                            RemediationId = action.Id,
                            RecipientKey = key,
                            Channel = channel,
                            LastLevel = action.Level,
                            LastExploitation = action.Exploitation,
                            FixNotified = action.FixAvailable,
                        });
                    }
                    else
                    {
                        state.LastLevel = Math.Max(state.LastLevel, action.Level);
                        state.LastExploitation = Exploitation.Rank(action.Exploitation) > Exploitation.Rank(state.LastExploitation) ? action.Exploitation : state.LastExploitation;
                        state.FixNotified |= action.FixAvailable;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Who hears about an event without a target naming them (master plan 8.2): the assignee about an
    /// assignment and about the team's work on their action, unless it is their own doing, and every member
    /// about a warning in the app.
    /// </summary>
    private static IEnumerable<(Recipient Recipient, string Channel)> Implicit(MessageItem item, bool ruleMatched, Context context)
    {
        switch (item.Event)
        {
            case NotifyEvents.ActionAssigned when Guid.TryParse(item.DataString("to"), out var assignee) && assignee != item.Actor && context.Directory.User(assignee) is { } person:
                yield return (person, Channels.InApp);
                if (context.EmailAvailable)
                {
                    yield return (person, Channels.Email);
                }

                break;
            case NotifyEvents.ActionStatusChanged or NotifyEvents.ActionNoteAdded
                when item.Action?.AssignedTo is { } assignee && assignee != item.Actor && context.Directory.User(assignee) is { } person:
                yield return (person, Channels.InApp);
                break;
            case NotifyEvents.WarningPublished when ruleMatched:
                foreach (var member in context.Directory.WithRole(OrganizationRole.Viewer))
                {
                    yield return (member, Channels.InApp);
                }

                break;
        }
    }

    /// <summary>The events that move the notification state; the others need only the uniqueness of the event (master plan 8.4).</summary>
    private static bool HasState(string kind) => kind is NotifyEvents.ActionRaised or NotifyEvents.ActionLevelRaised or NotifyEvents.ActionExploitationEscalated
        or NotifyEvents.ActionFixAvailable or NotifyEvents.ActionLevelLowered or NotifyEvents.ActionWithdrawn;

    /// <summary>The table of master plan 8.4: whether the recipient learns something new on this channel.</summary>
    internal static bool ShouldSend(MessageItem item, RecipientState? state, string channel) => item.Event switch
    {
        NotifyEvents.ActionRaised or NotifyEvents.ActionLevelRaised => (item.Level ?? 0) > (state?.LastLevel ?? 0),
        NotifyEvents.ActionExploitationEscalated => Exploitation.Rank(item.Action?.Exploitation) > Exploitation.Rank(state?.LastExploitation),
        NotifyEvents.ActionFixAvailable => !(state?.FixNotified ?? false),
        NotifyEvents.ActionLevelLowered or NotifyEvents.ActionWithdrawn => channel == Channels.InApp || state?.LastNotifiedAt is not null,
        _ => true,
    };

    private void Record(RecipientState? state, ActionFacts action, int level, string key, string channel, string kind, DateTimeOffset now)
    {
        if (state is null)
        {
            state = new RecipientState { RemediationId = action.Id, RecipientKey = key, Channel = channel };
            db.States.Add(state);
        }

        if (!NotifyEvents.IsAllClear(kind))
        {
            state.LastLevel = Math.Max(state.LastLevel, level);
            if (Exploitation.Rank(action.Exploitation) > Exploitation.Rank(state.LastExploitation))
            {
                state.LastExploitation = action.Exploitation;
            }

            state.FixNotified |= action.FixAvailable;
        }

        state.LastNotifiedAt = now;
    }

    private async Task LowerStatesAsync(Guid remediationId, int level, CancellationToken cancellationToken)
    {
        await db.States.Where(s => s.RemediationId == remediationId && s.LastLevel > level)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastLevel, level), cancellationToken);
        // States read earlier in this job are saved after every event; they now hold what the database holds.
        foreach (var tracked in db.States.Local.Where(s => s.RemediationId == remediationId && s.LastLevel > level))
        {
            db.Entry(tracked).Property(x => x.LastLevel).OriginalValue = level;
            tracked.LastLevel = level;
        }
    }

    private async Task NoteForDigestAsync(RuleTarget target, MessageItem item, CancellationToken cancellationToken)
    {
        if (db.DigestItems.Local.Any(i => i.RuleTargetId == target.Id && i.EventId == item.EventId)
            || await db.DigestItems.AnyAsync(i => i.RuleTargetId == target.Id && i.EventId == item.EventId, cancellationToken))
        {
            return;
        }

        db.DigestItems.Add(new DigestItem
        {
            Id = Guid.CreateVersion7(),
            RuleTargetId = target.Id,
            EventId = item.EventId,
            Event = item.Event,
            RemediationId = item.Action?.Id,
            EntryId = item.Warning?.EntryId,
            ProjectId = item.Backfill?.ProjectId,
            Level = item.Level,
            OccurredAt = item.At,
        });
    }

    private async Task SendAsync(Recipient recipient, string channel, MessageItem item, Context context, CancellationToken cancellationToken)
    {
        if (channel == Channels.InApp)
        {
            await InAppAsync(recipient, item, context);
            return;
        }

        var waiting = await WaitingAsync(recipient.Key, channel, cancellationToken);
        if (waiting is null)
        {
            var overLimit = await db.Deliveries.CountAsync(
                d => d.Kind == "immediate" && d.Channel == channel && d.State != "suppressed" && d.CreatedAt > context.Now - TimeSpan.FromHours(1),
                cancellationToken) >= HourlyLimit;
            var delivery = new Delivery
            {
                Id = Guid.CreateVersion7(),
                Kind = "immediate",
                Channel = channel,
                RecipientKey = recipient.Key,
                SendAfter = context.Now + Bundling,
                State = overLimit ? "suppressed" : "pending",
                LastError = overLimit ? $"More than {HourlyLimit} immediate messages on this channel within an hour; the event waits for the digest." : null,
                CreatedAt = context.Now,
            };
            db.Deliveries.Add(delivery);
            await db.SaveChangesAsync(cancellationToken);
            if (!overLimit)
            {
                await jobs.EnqueueAsync(new Deliver(delivery.Id), Deliver.Options(delivery.Id, delivery.SendAfter), cancellationToken);
            }

            waiting = delivery.Id;
        }

        db.DeliveryEvents.Add(EventOf(waiting.Value, recipient.Key, channel, item));
    }

    private async Task InAppAsync(Recipient recipient, MessageItem item, Context context)
    {
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            Kind = "immediate",
            Channel = Channels.InApp,
            RecipientKey = recipient.Key,
            SendAfter = context.Now,
            State = "sent",
            Attempts = 1,
            SentAt = context.Now,
            CreatedAt = context.Now,
        };
        db.Deliveries.Add(delivery);
        db.DeliveryEvents.Add(EventOf(delivery.Id, recipient.Key, Channels.InApp, item));
        var content = new MessageContent
        {
            DeliveryId = delivery.Id,
            Kind = "immediate",
            Organization = context.Organization,
            Locale = context.Directory.Locale(recipient, context.Organization.Locale),
            PublicUrl = context.PublicUrl,
            Items = [item],
            RecipientUserId = recipient.UserId,
        };
        var text = MessageRenderer.InApp(item, content);
        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(),
            UserId = recipient.Id,
            EventId = item.EventId,
            RemediationId = item.Action?.Id,
            EntryId = item.Warning?.EntryId,
            ProjectId = item.Backfill?.ProjectId,
            Kind = item.Event,
            Level = item.Level,
            Title = text.Title,
            Body = text.Body,
            Link = text.Link,
            CreatedAt = context.Now,
        });
        await Task.CompletedTask;
    }

    /// <summary>
    /// The waiting message of this recipient and channel, locked: the delivery job takes it under the same lock
    /// when it starts sending, so an event joins either before the message is read or a new message.
    /// </summary>
    private async Task<Guid?> WaitingAsync(string key, string channel, CancellationToken cancellationToken)
    {
        var organization = tenant.RequiredOrganizationId;
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM notify.delivery
            WHERE organization_id = {organization} AND kind = 'immediate' AND state = 'pending' AND recipient_key = {key} AND channel = {channel}
            FOR UPDATE
            """).ToListAsync(cancellationToken);
        return ids.Count == 0 ? null : ids[0];
    }

    private static DeliveryEvent EventOf(Guid delivery, string key, string channel, MessageItem item) => new()
    {
        Id = Guid.CreateVersion7(),
        DeliveryId = delivery,
        RecipientKey = key,
        Channel = channel,
        EventId = item.EventId,
        Event = item.Event,
        RemediationId = item.Action?.Id,
        EntryId = item.Warning?.EntryId,
        ProjectId = item.Backfill?.ProjectId,
        Level = item.Level,
        OccurredAt = item.At,
    };

    private static bool Usable(RuleTarget target, Context context) => target.Channel switch
    {
        Channels.InApp or Channels.Webhook => true,
        // Push reaches the members with a subscribed device, at once; the directory leaves out the others.
        Channels.Push => target.Mode == "immediate",
        // Without SMTP the channel is off (master plan 8.5); the rules page says so.
        Channels.Email => context.EmailAvailable,
        _ => false,
    };

    internal sealed record Batch(Context Context, Dictionary<Guid, ActionEventFacts> Events, Dictionary<Guid, ActionFacts> Actions);

    internal sealed record Context(
        OrganizationProfile Organization, IReadOnlyList<NotificationRule> Rules, RecipientDirectory Directory, bool EmailAvailable, Uri PublicUrl, DateTimeOffset Now);
}
