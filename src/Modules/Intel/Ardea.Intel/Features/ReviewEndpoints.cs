using Ardea.Intel.Persistence;
using Ardea.Platform.Audit;
using Ardea.Platform.Http;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Intel.Features;

internal sealed record ReviewEntryRef(string PrimaryKey, string? Title, string State);

internal sealed record ReviewItemView(
    Guid Id,
    string Kind,
    string State,
    IReadOnlyList<ReviewEntryRef> Entries,
    IReadOnlyList<string> SharedAliases,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? DecidedAt);

internal static class ReviewEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/instance/review").WithTags("Sources").RequireAuthorization(Policies.InstanceAdmin);
        admin.MapGet("/", ListAsync).WithName("listReviewItems").Produces<IReadOnlyList<ReviewItemView>>().Problems(400);
        admin.MapPost("/{id:guid}/keep-separate", KeepSeparateAsync).WithName("keepReviewItemSeparate").NoContent().Problems(404);
    }

    private static async Task<IResult> ListAsync(string? state, int? limit, IntelDbContext db, CancellationToken cancellationToken)
    {
        state ??= "open";
        if (state is not ("open" or "kept_separate"))
        {
            return ApiProblems.Validation("validation_failed", "Unknown state.", new Dictionary<string, string[]> { ["state"] = ["Use open or kept_separate."] });
        }

        var items = await db.ReviewItems.AsNoTracking().Where(i => i.State == state)
            .OrderByDescending(i => i.LastSeenAt).ThenBy(i => i.Id).Take(Math.Clamp(limit ?? 100, 1, 500)).ToListAsync(cancellationToken);
        var ids = items.SelectMany(i => i.EntryIds).Distinct().ToArray();
        var entries = await db.Entries.AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.PrimaryKey, e.Title, e.State }).ToDictionaryAsync(e => e.Id, cancellationToken);
        return Results.Ok(items.Select(i => new ReviewItemView(
            i.Id,
            i.Kind,
            i.State,
            [.. i.EntryIds.Where(entries.ContainsKey).Select(id => new ReviewEntryRef(entries[id].PrimaryKey, entries[id].Title, entries[id].State))],
            i.SharedAliases,
            i.FirstSeenAt,
            i.LastSeenAt,
            i.DecidedAt)).ToList());
    }

    private static async Task<IResult> KeepSeparateAsync(
        Guid id, IntelDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        var item = await db.ReviewItems.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return ApiProblems.Problem("not_found", StatusCodes.Status404NotFound, "No such review item.");
        }

        if (item.State == "open")
        {
            item.State = "kept_separate";
            item.DecidedAt = time.GetUtcNow();
            item.DecidedBy = tenant.UserId;
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(new AuditEntry("instance.review_kept_separate", "review_item", item.SubjectKey) { InstanceEvent = true }, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return Results.NoContent();
    }
}
