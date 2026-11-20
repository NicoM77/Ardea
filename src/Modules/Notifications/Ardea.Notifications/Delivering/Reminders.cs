using Ardea.Assessment.Contracts;
using Ardea.Iam.Contracts;
using Ardea.Notifications.Evaluation;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Email;
using Ardea.Platform.Jobs;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Notifications.Delivering;

/// <summary>
/// The rhythm of the reminders by level (master plan 8.7, 17.5; ADR 048): an open reminder reminds its recipients daily at
/// level 5, weekly at 4 and monthly at 3 by default, each configurable or off per organisation; below level 3 only the
/// digests name it.
/// </summary>
internal static class ReminderRhythms
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Off = "off";

    public static readonly IReadOnlyList<string> All = [Daily, Weekly, Monthly, Off];

    /// <summary>Weekdays at 08:00 in the organisation's time zone: never at night, never at the weekend.</summary>
    public const string Schedule = "0 8 * * 1-5";

    /// <summary>Below this level a reminder is not reminded of; the digests name it.</summary>
    public const int MinLevel = 3;

    /// <summary>
    /// A reminder is due this much before its rhythm is complete, so a run at 08:00 takes one raised at 09:00 the day (or week,
    /// or month) before instead of leaving it to the next run.
    /// </summary>
    public static readonly TimeSpan Slack = TimeSpan.FromHours(12);

    /// <summary>The rhythm the organisation set for a level.</summary>
    public static string Of(ReminderSchedule schedule, int level) => level switch
    {
        >= 5 => schedule.Level5,
        4 => schedule.Level4,
        3 => schedule.Level3,
        _ => Off,
    };

    /// <summary>
    /// Whether a reminder is due in a run at <paramref name="now"/>: one rhythm after it reached its level, or after it last
    /// reminded, whichever is later. A reminder whose level dropped counts its new rhythm from its last reminder.
    /// </summary>
    public static bool IsDue(string rhythm, DateTimeOffset since, DateTimeOffset? lastReminded, DateTimeOffset now)
    {
        var anchor = lastReminded is { } last && last > since ? last : since;
        DateTimeOffset? next = rhythm switch
        {
            Daily => anchor.AddDays(1),
            Weekly => anchor.AddDays(7),
            Monthly => anchor.AddMonths(1),
            _ => null,
        };
        return next is { } at && at - Slack <= now;
    }

    /// <summary>Reminders reach people: in the app, by e-mail and by push; webhooks hear about changes, not about what still stands.</summary>
    public static bool Reaches(string channel) => channel is Channels.InApp or Channels.Email or Channels.Push;
}

/// <summary>One run of the organisation's reminders by level (ADR 048), queued when its schedule is due.</summary>
internal sealed record SendReminders : IJob
{
    public static string Kind => "notify.send-reminders";

    public static EnqueueOptions Options(Guid organizationId) =>
        new() { CoalesceKey = $"notify.send-reminders:{organizationId}", OrganizationId = organizationId };
}

/// <summary>
/// The run (ADR 048), in one unit of work of the organisation: takes the schedule under its lock and checks it is still due,
/// finds the open reminders whose rhythm is complete, resolves who hears about each (the targets of the enabled rules that
/// announce the reminder: <c>action.raised</c>, its level and project), and writes one message per recipient and channel
/// with every reminder due for them. The messages go out through <see cref="Deliver"/> like any other; the run itself
/// talks to nobody outside.
/// </summary>
internal sealed class SendRemindersHandler(
    NotifyDbContext db,
    IActionFacts facts,
    IOrganizationProfiles organizations,
    IOrganizationMembers members,
    IEmailSender email,
    IJobQueue jobs,
    ITenantContext tenant,
    TimeProvider time) : IJobHandler<SendReminders>
{
    public async Task HandleAsync(SendReminders job, JobContext context, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var organization = tenant.RequiredOrganizationId;
        var profile = await organizations.FindAsync(organization, cancellationToken);
        if (profile is null)
        {
            return;
        }

        // Organisations from before, or created by the previous build during an upgrade, get the defaults with their first run.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO notify.reminder_schedule (organization_id, level5, level4, level3, changed_at)
            VALUES ({organization}, {ReminderRhythms.Daily}, {ReminderRhythms.Weekly}, {ReminderRhythms.Monthly}, {now})
            ON CONFLICT (organization_id) DO NOTHING
            """, cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM notify.reminder_schedule WHERE organization_id = {organization} FOR UPDATE", cancellationToken);
        var schedule = await db.ReminderSchedules.SingleAsync(cancellationToken);
        if (schedule.NextRunAt is not { } runAt)
        {
            schedule.NextRunAt = DigestSchedule.Next(ReminderRhythms.Schedule, profile.TimeZone, now);
            schedule.LastRunAt ??= now;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Checked under the lock: a second job for the same run finds it done.
        if (runAt > now)
        {
            return;
        }

        await RemindAsync(schedule, profile, runAt, now, cancellationToken);
        schedule.LastRunAt = runAt;
        // A worker that was down sends one run for the gap, not one per missed weekday.
        schedule.NextRunAt = DigestSchedule.Next(ReminderRhythms.Schedule, profile.TimeZone, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RemindAsync(ReminderSchedule schedule, OrganizationProfile profile, DateTimeOffset runAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var open = await facts.OpenRemindersAsync(ReminderRhythms.MinLevel, cancellationToken);
        if (open.Count == 0)
        {
            return;
        }

        var states = await db.ReminderStates.ToDictionaryAsync(s => s.RemediationId, cancellationToken);
        var due = open.Where(r => ReminderRhythms.IsDue(ReminderRhythms.Of(schedule, r.Level), r.Since, states.GetValueOrDefault(r.Id)?.LastRemindedAt, now)).ToList();
        if (due.Count == 0)
        {
            return;
        }

        var rules = (await db.Rules.AsNoTracking().Include(r => r.Targets).Where(r => r.Enabled).ToListAsync(cancellationToken))
            .Where(r => r.Events.Contains(NotifyEvents.ActionRaised)).ToList();
        var directory = await RecipientDirectory.LoadAsync(db, members, cancellationToken);
        var emailAvailable = await email.IsConfiguredAsync(cancellationToken);
        var messages = new Dictionary<(string Key, string Channel), List<OpenReminder>>();
        foreach (var reminder in due)
        {
            var targets = rules.Where(r => r.MinLevel <= reminder.Level && (r.ProjectIds.Length == 0 || r.ProjectIds.Contains(reminder.ProjectId)))
                .SelectMany(r => r.Targets)
                .Where(t => ReminderRhythms.Reaches(t.Channel) && (t.Channel != Channels.Email || emailAvailable));
            foreach (var target in targets)
            {
                foreach (var recipient in directory.Resolve(target))
                {
                    if (directory.IsMuted(recipient, target.Channel, reminder.Level, profile.AllowMuteCritical))
                    {
                        continue;
                    }

                    var list = messages.TryGetValue((recipient.Key, target.Channel), out var found) ? found : messages[(recipient.Key, target.Channel)] = [];
                    if (!list.Contains(reminder))
                    {
                        list.Add(reminder);
                    }
                }
            }
        }

        // Each reminder is one event of this run, the same on every channel and for every recipient.
        var events = due.ToDictionary(r => r.Id, _ => Guid.CreateVersion7());
        foreach (var ((key, channel), reminders) in messages)
        {
            if (await db.Deliveries.AnyAsync(d => d.Kind == "reminder" && d.RecipientKey == key && d.Channel == channel && d.PeriodEnd == runAt, cancellationToken))
            {
                continue;
            }

            var delivery = new Delivery
            {
                Id = Guid.CreateVersion7(),
                Kind = "reminder",
                Channel = channel,
                RecipientKey = key,
                SendAfter = now,
                State = "pending",
                CreatedAt = now,
                PeriodStart = schedule.LastRunAt,
                PeriodEnd = runAt,
            };
            db.Deliveries.Add(delivery);
            foreach (var reminder in reminders)
            {
                db.DeliveryEvents.Add(new DeliveryEvent
                {
                    Id = Guid.CreateVersion7(),
                    DeliveryId = delivery.Id,
                    RecipientKey = key,
                    Channel = channel,
                    EventId = events[reminder.Id],
                    Event = NotifyEvents.ReminderDue,
                    RemediationId = reminder.Id,
                    Level = reminder.Level,
                    OccurredAt = reminder.Since,
                });
            }

            await jobs.EnqueueAsync(new Deliver(delivery.Id), Deliver.Options(delivery.Id, now), cancellationToken);
        }

        foreach (var reminder in due)
        {
            if (states.TryGetValue(reminder.Id, out var state))
            {
                state.LastRemindedAt = now;
                state.Level = reminder.Level;
            }
            else
            {
                db.ReminderStates.Add(new ReminderState { RemediationId = reminder.Id, LastRemindedAt = now, Level = reminder.Level });
            }
        }
    }
}
