using Ardea.Notifications.Delivering;
using Ardea.Notifications.Persistence;
using Ardea.Platform.Audit;
using Ardea.Platform.Http;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Notifications.Features;

/// <summary>
/// How often open reminders remind, by level (master plan 8.7, 17.5; ADR 048).
/// </summary>
/// <param name="Level5"><c>daily</c>, <c>weekly</c>, <c>monthly</c> or <c>off</c>; daily by default.</param>
/// <param name="Level4">Weekly by default.</param>
/// <param name="Level3">Monthly by default.</param>
/// <param name="NextRunAt">The next run, weekdays at 08:00 in the organisation's time zone; <c>null</c> before the first.</param>
internal sealed record ReminderRhythmView(string Level5, string Level4, string Level3, DateTimeOffset? NextRunAt);

/// <summary>Sets the rhythm per level; each of <c>daily</c>, <c>weekly</c>, <c>monthly</c> or <c>off</c>.</summary>
internal sealed record ReminderRhythmRequest(string? Level5, string? Level4, string? Level3);

/// <summary>
/// The organisation's reminders by level (ADR 048): owners and admins read and set the rhythm, like the rules. Levels 1 and 2
/// have none: below level 3 only the digests name a reminder.
/// </summary>
internal static class ReminderEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var org = api.MapOrganization().WithTags("Notifications").MapGroup("/reminder-rhythm").RequireOrganizationRole(OrganizationRole.Admin);
        org.MapGet("/", GetAsync).WithName("getReminderRhythm").Produces<ReminderRhythmView>().Problems(403, 404);
        org.MapPut("/", SetAsync).WithName("setReminderRhythm").Produces<ReminderRhythmView>().Problems(400, 403, 404);
    }

    private static async Task<ReminderRhythmView> GetAsync(NotifyDbContext db, CancellationToken cancellationToken) =>
        View(await db.ReminderSchedules.AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new ReminderSchedule());

    private static async Task<IResult> SetAsync(
        ReminderRhythmRequest request, NotifyDbContext db, ITenantContext tenant, IAuditLog audit, IUnitOfWork unitOfWork, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (field, value) in new[] { ("level5", request.Level5), ("level4", request.Level4), ("level3", request.Level3) })
        {
            if (value is null || !ReminderRhythms.All.Contains(value))
            {
                errors[field] = [$"Choose {string.Join(", ", ReminderRhythms.All)}."];
            }
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation("invalid_reminder_rhythm", "The reminder rhythm is not valid.", errors);
        }

        var now = time.GetUtcNow();
        var organization = tenant.RequiredOrganizationId;
        // Under the row's lock, as a run takes it: a run sees the rhythm before or after, never half of it.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO notify.reminder_schedule (organization_id, level5, level4, level3, changed_at)
            VALUES ({organization}, {request.Level5}, {request.Level4}, {request.Level3}, {now})
            ON CONFLICT (organization_id) DO UPDATE SET level5 = excluded.level5, level4 = excluded.level4, level3 = excluded.level3, changed_at = excluded.changed_at
            """, cancellationToken);
        await audit.RecordAsync(
            new AuditEntry("notify.reminder_rhythm_changed", "organization", organization.ToString(), new { request.Level5, request.Level4, request.Level3 }),
            cancellationToken);
        var view = View(await db.ReminderSchedules.AsNoTracking().SingleAsync(cancellationToken));
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(view);
    }

    private static ReminderRhythmView View(ReminderSchedule schedule) => new(schedule.Level5, schedule.Level4, schedule.Level3, schedule.NextRunAt);
}
