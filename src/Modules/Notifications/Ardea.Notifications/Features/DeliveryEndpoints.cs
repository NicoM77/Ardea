using Ardea.Iam.Contracts;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Http;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Notifications.Features;

/// <summary>The delivery log (master plan 8.6, 10.2): one row per message, newest first; owners and admins.</summary>
internal static class DeliveryEndpoints
{
    private static readonly string[] States = ["pending", "sending", "sent", "failed", "dead", "suppressed"];

    public static void Map(IEndpointRouteBuilder api) =>
        api.MapOrganization().WithTags("Notifications")
            .MapGet("/deliveries", ListAsync).WithName("listDeliveries").RequireOrganizationRole(OrganizationRole.Admin)
            .Produces<DeliveryListView>().Problems(400, 403, 404);

    private static async Task<IResult> ListAsync(
        string? state, string? channel, int? limit, string? cursor, NotifyDbContext db, IOrganizationMembers members, CancellationToken cancellationToken)
    {
        if ((state is not null && !States.Contains(state)) || (channel is not null && !Channels.Usable.Contains(channel))
            || limit is < 1 or > 200 || !Paging.TryOffset(cursor, out var offset))
        {
            return ApiProblems.Validation("invalid_filter", "The filter is not valid.", new Dictionary<string, string[]>
            {
                ["filter"] = [$"state: {string.Join(", ", States)}; channel: {string.Join(", ", Channels.Usable)}; limit 1 to 200."],
            });
        }

        var query = db.Deliveries.AsNoTracking();
        if (state is not null)
        {
            query = query.Where(d => d.State == state);
        }

        if (channel is not null)
        {
            query = query.Where(d => d.Channel == channel);
        }

        var take = limit ?? 50;
        var rows = await query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id).Skip(offset).Take(take + 1).ToListAsync(cancellationToken);
        var ids = rows.Select(d => d.Id).ToArray();
        var counts = await db.DeliveryEvents.AsNoTracking().Where(e => ids.Contains(e.DeliveryId)).GroupBy(e => e.DeliveryId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in await members.ListAsync(cancellationToken))
        {
            labels[RecipientKeys.User(m.UserId)] = m.DisplayName;
        }

        foreach (var a in await db.Addresses.AsNoTracking().Select(a => new { a.Id, a.Address }).ToListAsync(cancellationToken))
        {
            labels[RecipientKeys.Address(a.Id)] = a.Address;
        }

        foreach (var e in await db.Endpoints.AsNoTracking().Select(e => new { e.Id, e.Name }).ToListAsync(cancellationToken))
        {
            labels[RecipientKeys.Endpoint(e.Id)] = e.Name;
        }

        var items = rows.Take(take).Select(d =>
        {
            var (kind, id) = RecipientKeys.Parse(d.RecipientKey);
            return new DeliveryView(
                d.Id, d.Kind, d.Channel, new DeliveryRecipientView(kind, id, labels.GetValueOrDefault(d.RecipientKey, string.Empty)), d.State, d.Attempts,
                d.LastError, counts.GetValueOrDefault(d.Id), d.SendAfter, d.SentAt, d.CreatedAt, d.PeriodStart, d.PeriodEnd);
        }).ToList();
        return Results.Ok(new DeliveryListView(items, Paging.Next(offset, take, rows.Count)));
    }
}
