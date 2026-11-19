using Ardea.Iam.Contracts;
using Ardea.Notifications.Persistence;

namespace Ardea.Notifications.Rules;

/// <summary>
/// The three rules every new organisation starts with (master plan 8.3). They overlap on purpose; the
/// state per recipient and channel keeps them from sending the same twice (8.4). Push joins rule 1 with
/// task 5.5. The migration <c>DefaultRulesForExistingOrganizations</c> writes the same for organisations
/// created before.
/// </summary>
internal sealed class DefaultRules(NotifyDbContext db, IOrganizationProfiles organizations, TimeProvider time) : IOrganizationCreatedSubscriber
{
    /// <summary>Weekdays at 08:00 in the organisation's time zone.</summary>
    public const string Weekdays = "0 8 * * 1-5";

    /// <summary>Mondays at 08:00 in the organisation's time zone.</summary>
    public const string Mondays = "0 8 * * 1";

    private static readonly string[] ActionEvents =
    [
        NotifyEvents.ActionRaised, NotifyEvents.ActionLevelRaised, NotifyEvents.ActionExploitationEscalated,
        NotifyEvents.ActionLevelLowered, NotifyEvents.ActionWithdrawn, NotifyEvents.BackfillCompleted,
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
                Id = id, Name = name, Preset = preset, MinLevel = minLevel, Events = events, Targets = [.. targets], CreatedAt = now, ChangedAt = now,
            };
        }

        return
        [
            Rule("critical", "Critical at once", 5, ActionEvents,
                Target(Channels.InApp, "admin", "immediate"),
                Target(Channels.Email, "admin", "immediate")),
            Rule("high", "High", 4, ActionEvents,
                Target(Channels.InApp, "viewer", "immediate"),
                Target(Channels.Email, "viewer", "digest", Weekdays)),
            Rule("weekly", "Weekly summary", 2, [.. ActionEvents, NotifyEvents.WarningPublished],
                Target(Channels.Email, "viewer", "digest", Mondays, sendEmpty: true)),
        ];
    }
}
