using Ardea.Iam.Contracts;
using Ardea.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Notifications.Rules;

internal sealed class DefaultRules(NotifyDbContext db, IOrganizationProfiles organizations, TimeProvider time) : IOrganizationCreatedSubscriber
{
    public const string Weekdays = "0 8 * * 1-5";

    public const string Mondays = "0 8 * * 1";

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

    public static Task<int> NameNoticesAsync(NotifyDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            UPDATE notify.rule SET events = array_append(events, 'notice.raised'), changed_at = now()
            WHERE preset IN ('critical', 'high', 'weekly') AND changed_at = created_at AND NOT ('notice.raised' = ANY (events))
            """,
            cancellationToken);

    public static IReadOnlyList<NotificationRule> Create(DateTimeOffset now, string timeZone)
    {
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

internal sealed class DigestRescheduler(NotifyDbContext db, TimeProvider time) : IOrganizationTimeZoneSubscriber
{
    public async Task OnTimeZoneChangedAsync(Guid organizationId, string timeZone, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        foreach (var target in await db.Targets.Where(t => t.Mode == "digest" && t.DigestCron != null).ToListAsync(cancellationToken))
        {
            target.NextDigestAt = DigestSchedule.Next(target.DigestCron!, timeZone, now);
        }

        foreach (var schedule in await db.ReminderSchedules.Where(s => s.NextRunAt != null).ToListAsync(cancellationToken))
        {
            schedule.NextRunAt = DigestSchedule.Next(Delivering.ReminderRhythms.Schedule, timeZone, now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
