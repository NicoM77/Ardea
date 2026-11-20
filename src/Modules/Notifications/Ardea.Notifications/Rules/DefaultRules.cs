using Ardea.Iam.Contracts;
using Ardea.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Notifications.Rules;

/// <summary>
/// The three rules every new organisation starts with (master plan 8.3). They overlap on purpose; the
/// state per recipient and channel keeps them from sending the same twice (8.4). The migrations
/// <c>InitialNotify</c> and <c>WebPush</c> (the push target of rule 1) write the same for organisations created before.
/// </summary>
internal sealed class DefaultRules(NotifyDbContext db, IOrganizationProfiles organizations, TimeProvider time) : IOrganizationCreatedSubscriber
{
    /// <summary>Weekdays at 08:00 in the organisation's time zone.</summary>
    public const string Weekdays = "0 8 * * 1-5";

    /// <summary>Mondays at 08:00 in the organisation's time zone.</summary>
    public const string Mondays = "0 8 * * 1";

    /// <summary>
    /// The events of every default rule. A notice has no level: it meets every threshold when it is actively exploited or in
    /// KEV, else only level 2, so rule 1 brings the important ones at once and the weekly summary all of them (ADR 048).
    /// </summary>
    private static readonly string[] ActionEvents =
    [
        NotifyEvents.ActionRaised, NotifyEvents.ActionLevelRaised, NotifyEvents.ActionExploitationEscalated,
        NotifyEvents.ActionLevelLowered, NotifyEvents.ActionWithdrawn, NotifyEvents.BackfillCompleted, NotifyEvents.NoticeRaised,
    ];

    public async Task OnOrganizationCreatedAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var timeZone = (await organizations.FindAsync(organizationId, cancellationToken))?.TimeZone ?? "UTC";
        foreach (var rule in Create(time.GetUtcNow(), timeZone))
        {
            db.Rules.Add(rule);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The module's step after every <c>migrate</c> (ADR 048): default rules nobody edited (<c>changed_at = created_at</c>)
    /// learn <c>notice.raised</c>, also those the previous build creates during an upgrade, at the next migrate. A preset an
    /// admin changed, narrowed or switched off stays as it is: it may name one person now. Idempotent; the rule counts as
    /// changed afterwards, so a later edit that removes the event is kept.
    /// </summary>
    public static Task<int> NameNoticesAsync(NotifyDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            UPDATE notify.rule SET events = array_append(events, 'notice.raised'), changed_at = now()
            WHERE preset IN ('critical', 'high', 'weekly') AND changed_at = created_at AND NOT ('notice.raised' = ANY (events))
            """,
            cancellationToken);

    public static IReadOnlyList<NotificationRule> Create(DateTimeOffset now, string timeZone)
    {
        // One microsecond apart, the database's precision: lists show targets in the order written here.
        var order = 0;
        RuleTarget Target(string channel, string role, string mode, string? cron = null, bool sendEmpty = false) => new()
        {
            Id = Guid.CreateVersion7(),
            Channel = channel,
            RecipientKind = RecipientKinds.Role,
            RecipientRef = role,
            Mode = mode,
            DigestCron = cron,
            SendEmpty = sendEmpty,
            NextDigestAt = cron is null ? null : DigestSchedule.Next(cron, timeZone, now),
            LastDigestAt = cron is null ? null : now,
            CreatedAt = now.AddTicks(++order * 10),
        };

        NotificationRule Rule(string preset, string name, int minLevel, string[] events, params RuleTarget[] targets)
        {
            var id = Guid.CreateVersion7();
            foreach (var target in targets)
            {
                target.RuleId = id;
            }

            return new NotificationRule
            {
                Id = id,
                Name = name,
                Preset = preset,
                MinLevel = minLevel,
                Events = events,
                Targets = [.. targets],
                CreatedAt = now,
                ChangedAt = now,
            };
        }

        return
        [
            Rule("critical", "Critical at once", 5, ActionEvents,
                Target(Channels.InApp, "admin", "immediate"),
                Target(Channels.Email, "admin", "immediate"),
                Target(Channels.Push, "admin", "immediate")),
            Rule("high", "High", 4, ActionEvents,
                Target(Channels.InApp, "viewer", "immediate"),
                Target(Channels.Email, "viewer", "digest", Weekdays)),
            Rule("weekly", "Weekly summary", 2, [.. ActionEvents, NotifyEvents.WarningPublished],
                Target(Channels.Email, "viewer", "digest", Mondays, sendEmpty: true)),
        ];
    }
}

/// <summary>Moves the next digest of every target, and the next run of the reminders, to the time their schedule names in the new time zone (master plan 8.3).</summary>
internal sealed class DigestRescheduler(NotifyDbContext db, TimeProvider time) : IOrganizationTimeZoneSubscriber
{
    public async Task OnTimeZoneChangedAsync(Guid organizationId, string timeZone, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        foreach (var target in await db.Targets.Where(t => t.Mode == "digest" && t.DigestCron != null).ToListAsync(cancellationToken))
        {
            target.NextDigestAt = DigestSchedule.Next(target.DigestCron!, timeZone, now);
        }

        // The reminders by level keep their quiet hour in the new zone too (ADR 048).
        foreach (var schedule in await db.ReminderSchedules.Where(s => s.NextRunAt != null).ToListAsync(cancellationToken))
        {
            schedule.NextRunAt = DigestSchedule.Next(Delivering.ReminderRhythms.Schedule, timeZone, now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
