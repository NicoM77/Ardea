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

internal static class ReminderRhythms
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Off = "off";

    public static readonly IReadOnlyList<string> All = [Daily, Weekly, Monthly, Off];

    public const string Schedule = "0 8 * * 1-5";

    public const int MinLevel = 3;

    public static readonly TimeSpan Slack = TimeSpan.FromHours(12);

    public static string Of(ReminderSchedule schedule, int level) => level switch
    {
        >= 5 => schedule.Level5,
        4 => schedule.Level4,
        3 => schedule.Level3,
        _ => Off,
    };

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

    public static bool Reaches(string channel) => channel is Channels.InApp or Channels.Email or Channels.Push;
}

internal sealed record SendReminders : IJob
{
    public static string Kind => "notify.send-reminders";

    public static EnqueueOptions Options(Guid organizationId) =>
        new() { CoalesceKey = $"notify.send-reminders:{organizationId}", OrganizationId = organizationId };
}

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

        if (runAt > now)
        {
            return;
        }

        await RemindAsync(schedule, profile, runAt, now, cancellationToken);
        schedule.LastRunAt = runAt;
        schedule.NextRunAt = DigestSchedule.Next(ReminderRhythms.Schedule, profile.TimeZone, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RemindAsync(ReminderSchedule schedule, OrganizationProfile profile, DateTimeOffset runAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var open = await facts.OpenRemindersAsync(ReminderRhythms.MinLevel, cancellationToken);
        var openIds = open.Select(r => r.Id).ToArray();
        await db.ReminderStates.Where(s => !openIds.Contains(s.RemediationId)).ExecuteDeleteAsync(cancellationToken);
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

        foreach (var reminder in messages.Values.SelectMany(r => r).DistinctBy(r => r.Id))
        {
            if (states.TryGetValue(reminder.Id, out var state))
            {
                state.LastRemindedAt = now;
            }
            else
            {
                db.ReminderStates.Add(new ReminderState { RemediationId = reminder.Id, LastRemindedAt = now });
            }
        }
    }
}
