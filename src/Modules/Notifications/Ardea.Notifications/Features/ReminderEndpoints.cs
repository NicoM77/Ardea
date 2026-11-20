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

internal sealed record ReminderRhythmView(string Level5, string Level4, string Level3, DateTimeOffset? NextRunAt);

internal sealed record ReminderRhythmRequest(string? Level5, string? Level4, string? Level3);

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
