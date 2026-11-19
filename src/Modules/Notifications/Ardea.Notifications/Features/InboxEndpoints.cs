using Ardea.Iam.Contracts;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Http;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Notifications.Features;

/// <summary>
/// The signed-in member's own notifications and settings in one organisation (master plan 8.3, 8.5, 10.2;
/// tasks 5.3, 5.7). Every member, viewers too, sees only their own rows: the queries name the user, and the
/// row-level policy on these user tables does the same. New notifications wake the live stream of the
/// organisation, whose <c>changed</c> event makes the web app reload the counter.
/// </summary>
internal static class InboxEndpoints
{
    private const int DefaultLimit = 30;

    public static void Map(IEndpointRouteBuilder api)
    {
        var org = api.MapOrganization().WithTags("Notifications");
        org.MapGet("/notifications", ListAsync).WithName("listInAppNotifications").Produces<InAppNotificationListView>().Problems(400, 403, 404);
        org.MapGet("/notifications/unread-count", UnreadAsync).WithName("countUnreadNotifications").Produces<UnreadCountView>().Problems(403, 404);
        org.MapPost("/notifications/read", MarkReadAsync).WithName("markNotificationsRead").Produces<UnreadCountView>().Problems(400, 403, 404);
        org.MapGet("/notification-preferences", PreferencesAsync).WithName("getNotificationPreferences").Produces<NotificationPreferencesView>().Problems(403, 404);
        org.MapPut("/notification-preferences", SetPreferencesAsync).WithName("setNotificationPreferences").Produces<NotificationPreferencesView>().Problems(400, 403, 404);
    }

    private static async Task<IResult> ListAsync(
        bool? unread, int? limit, string? cursor, NotifyDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var user = tenant.UserId!.Value;
        if (limit is < 1 or > 100 || !Paging.TryOffset(cursor, out var offset))
        {
            return ApiProblems.Validation("invalid_filter", "The filter is not valid.", new Dictionary<string, string[]> { ["limit"] = ["Choose 1 to 100, with a cursor from a previous page."] });
        }

        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == user);
        if (unread == true)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var take = limit ?? DefaultLimit;
        var rows = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Skip(offset).Take(take + 1).ToListAsync(cancellationToken);
        var page = rows.Take(take).Select(n => new InAppNotificationView(n.Id, n.Kind, n.Level, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt)).ToList();
        var count = await db.Notifications.CountAsync(n => n.UserId == user && n.ReadAt == null, cancellationToken);
        return Results.Ok(new InAppNotificationListView(page, Paging.Next(offset, take, rows.Count), count));
    }

    private static async Task<UnreadCountView> UnreadAsync(NotifyDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var user = tenant.UserId!.Value;
        return new UnreadCountView(await db.Notifications.CountAsync(n => n.UserId == user && n.ReadAt == null, cancellationToken));
    }

    private static async Task<IResult> MarkReadAsync(
        MarkNotificationsReadRequest request, NotifyDbContext db, ITenantContext tenant, IUnitOfWork unitOfWork, TimeProvider time, CancellationToken cancellationToken)
    {
        var user = tenant.UserId!.Value;
        if (request.All != true && (request.Ids is not { Count: > 0 and <= 500 }))
        {
            return ApiProblems.Validation("invalid_request", "Name the notifications to mark, or all.", new Dictionary<string, string[]> { ["ids"] = ["Name 1 to 500 notifications, or set all."] });
        }

        var query = db.Notifications.Where(n => n.UserId == user && n.ReadAt == null);
        if (request.All != true)
        {
            var ids = request.Ids!.ToArray();
            query = query.Where(n => ids.Contains(n.Id));
        }

        var now = time.GetUtcNow();
        await query.ExecuteUpdateAsync(n => n.SetProperty(x => x.ReadAt, now), cancellationToken);
        var count = await db.Notifications.CountAsync(n => n.UserId == user && n.ReadAt == null, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(new UnreadCountView(count));
    }

    private static async Task<NotificationPreferencesView> PreferencesAsync(
        NotifyDbContext db, ITenantContext tenant, IOrganizationProfiles organizations, CancellationToken cancellationToken)
    {
        var user = tenant.UserId!.Value;
        var preference = await db.Preferences.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == user, cancellationToken);
        var allowCritical = (await organizations.FindAsync(tenant.RequiredOrganizationId, cancellationToken))?.AllowMuteCritical ?? false;
        return new NotificationPreferencesView(preference?.MutedChannels ?? [], preference?.Locale, allowCritical);
    }

    private static async Task<IResult> SetPreferencesAsync(
        NotificationPreferencesRequest request, NotifyDbContext db, ITenantContext tenant, IOrganizationProfiles organizations, IUnitOfWork unitOfWork,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var muted = (request.MutedChannels ?? []).Distinct(StringComparer.Ordinal).ToList();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (muted.Any(c => c is not (Channels.InApp or Channels.Email or Channels.Push)))
        {
            errors["mutedChannels"] = ["Choose from in_app, email and push."];
        }

        if (request.Locale is { Length: > 0 } locale && locale is not ("en" or "de" or "fr" or "it"))
        {
            errors["locale"] = ["Choose en, de, fr or it, or nothing to follow the organisation."];
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation("invalid_preferences", "The settings are not valid.", errors);
        }

        var user = tenant.UserId!.Value;
        var preference = await db.Preferences.SingleOrDefaultAsync(p => p.UserId == user, cancellationToken);
        if (preference is null)
        {
            preference = new UserPreference { UserId = user };
            db.Preferences.Add(preference);
        }

        preference.MutedChannels = [.. muted];
        preference.Locale = request.Locale is { Length: > 0 } chosen ? chosen : null;
        preference.ChangedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        var allowCritical = (await organizations.FindAsync(tenant.RequiredOrganizationId, cancellationToken))?.AllowMuteCritical ?? false;
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(new NotificationPreferencesView(preference.MutedChannels, preference.Locale, allowCritical));
    }
}

/// <summary>Cursor pages (master plan 10.1); the cursor is opaque to clients and counts the rows already shown.</summary>
internal static class Paging
{
    public static bool TryOffset(string? cursor, out int offset)
    {
        offset = 0;
        return cursor is null || (int.TryParse(cursor, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out offset) && offset <= 100_000);
    }

    public static string? Next(int offset, int take, int read) =>
        read > take ? (offset + take).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
}
