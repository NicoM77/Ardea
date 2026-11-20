using System.Text;
using Ardea.Notifications.Delivering;
using Ardea.Notifications.Evaluation;
using Ardea.Notifications.Messages;
using Ardea.Notifications.Persistence;
using Ardea.Platform.Http;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Notifications.Features;

internal sealed record PushPublicKeyView(string PublicKey);

internal sealed record PushSubscriptionView(Guid Id, string Label, DateTimeOffset CreatedAt, DateTimeOffset? LastSuccessAt, int FailedCount);

internal sealed record PushSubscriptionRequest(string? Endpoint, PushSubscriptionKeysRequest? Keys, double? ExpirationTime);

internal sealed record PushSubscriptionKeysRequest(string? P256dh, string? Auth);

internal sealed record PushTestView(bool Ok, string? Status, bool Removed);

internal static class PushEndpoints
{
    public const int DevicesPerUser = 20;

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/push/public-key", PublicKeyAsync).WithTags("Notifications").WithName("getPushPublicKey").RequireAuthorization()
            .Produces<PushPublicKeyView>();

        var me = api.MapGroup("/me/push-subscriptions").WithTags("Notifications").RequireAuthorization();
        me.MapGet("/", ListAsync).WithName("listPushSubscriptions").Produces<IReadOnlyList<PushSubscriptionView>>();
        me.MapPost("/", SubscribeAsync).WithName("createPushSubscription")
            .Produces<PushSubscriptionView>().Produces<PushSubscriptionView>(StatusCodes.Status201Created).Problems(400);
        me.MapDelete("/", UnsubscribeAsync).WithName("deletePushSubscriptionByEndpoint").NoContent();
        me.MapDelete("/{id:guid}", DeleteAsync).WithName("deletePushSubscription").NoContent().Problems(404);
        me.MapPost("/{id:guid}/test", TestAsync).WithName("testPushSubscription").RequireRateLimiting(RateLimits.Outbound).Produces<PushTestView>().Problems(404);
    }

    private static async Task<IResult> PublicKeyAsync(VapidKeyStore vapid, CancellationToken cancellationToken) =>
        Results.Ok(new PushPublicKeyView(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode((await vapid.GetAsync(cancellationToken)).PublicKey)));

    private static async Task<IReadOnlyList<PushSubscriptionView>> ListAsync(NotifyDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var user = tenant.UserId!.Value;
        return await db.PushSubscriptions.AsNoTracking().Where(s => s.UserId == user).OrderByDescending(s => s.CreatedAt)
            .Select(s => new PushSubscriptionView(s.Id, s.Label, s.CreatedAt, s.LastSuccessAt, s.FailedCount)).ToListAsync(cancellationToken);
    }

    private static async Task<IResult> SubscribeAsync(
        PushSubscriptionRequest request, HttpContext http, NotifyDbContext db, ITenantContext tenant, IUnitOfWork unitOfWork, IConfiguration configuration,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (WebPushChannel.CheckEndpoint(request.Endpoint, configuration) is { } endpointError)
        {
            errors["endpoint"] = [endpointError];
        }

        if (PushConfiguration.Decode(request.Keys?.P256dh) is not { Length: 65 } point || point[0] != 4 || !P256.IsPublicKey(point))
        {
            errors["keys.p256dh"] = ["The browser's key must be an uncompressed P-256 point, base64url."];
        }

        if (PushConfiguration.Decode(request.Keys?.Auth) is not { Length: 16 })
        {
            errors["keys.auth"] = ["The authentication secret must be 16 bytes, base64url."];
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation("invalid_push_subscription", "The push subscription is not valid.", errors);
        }

        var user = tenant.UserId!.Value;
        var endpoint = request.Endpoint!.Trim();
        var hash = WebPushChannel.Hash(endpoint);
        var userAgent = http.Request.Headers.UserAgent.ToString();
        var now = time.GetUtcNow();
        if (!await db.PushSubscriptions.AnyAsync(s => s.UserId == user && s.EndpointHash == hash, cancellationToken))
        {
            var oldest = await db.PushSubscriptions.Where(s => s.UserId == user).OrderByDescending(s => s.CreatedAt)
                .Skip(DevicesPerUser - 1).Select(s => s.Id).ToListAsync(cancellationToken);
            await db.PushSubscriptions.Where(s => oldest.Contains(s.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        var p256dh = request.Keys!.P256dh!.Trim();
        var auth = request.Keys.Auth!.Trim();
        var agent = userAgent.Length > 500 ? userAgent[..500] : userAgent;
        var label = DeviceLabel(userAgent);
        var created = (await db.Database.SqlQuery<bool>($"""
            INSERT INTO notify.push_subscription (id, user_id, endpoint, endpoint_hash, p256dh, auth, user_agent, label, created_at, failed_count, rejected_count)
            VALUES ({Guid.CreateVersion7()}, {user}, {endpoint}, {hash}, {p256dh}, {auth}, {agent}, {label}, {now}, 0, 0)
            ON CONFLICT (user_id, endpoint_hash) DO UPDATE SET p256dh = excluded.p256dh, auth = excluded.auth, user_agent = excluded.user_agent,
                label = excluded.label, failed_count = 0, rejected_count = 0
            RETURNING (xmax = 0) AS "Value"
            """).ToListAsync(cancellationToken))[0];
        var subscription = await db.PushSubscriptions.AsNoTracking().SingleAsync(s => s.UserId == user && s.EndpointHash == hash, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        var view = new PushSubscriptionView(subscription.Id, subscription.Label, subscription.CreatedAt, subscription.LastSuccessAt, subscription.FailedCount);
        return created ? Results.Created($"/api/v1/me/push-subscriptions/{subscription.Id}", view) : Results.Ok(view);
    }

    private static async Task<IResult> UnsubscribeAsync(string? endpoint, NotifyDbContext db, ITenantContext tenant, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            var user = tenant.UserId!.Value;
            var hash = WebPushChannel.Hash(endpoint.Trim());
            await db.PushSubscriptions.Where(s => s.UserId == user && s.EndpointHash == hash).ExecuteDeleteAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, NotifyDbContext db, ITenantContext tenant, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        var user = tenant.UserId!.Value;
        if (await db.PushSubscriptions.Where(s => s.UserId == user && s.Id == id).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            return NotFound();
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> TestAsync(
        Guid id, NotifyDbContext db, ITenantContext tenant, IUnitOfWork unitOfWork, IUnitOfWorkFactory units, WebPushChannel channel, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var user = tenant.UserId!.Value;
        var device = await db.PushSubscriptions.AsNoTracking().Where(s => s.UserId == user && s.Id == id)
            .Select(s => new PushDevice(s.Id, s.Endpoint, s.P256dh, s.Auth)).SingleOrDefaultAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        if (device is null)
        {
            return NotFound();
        }

        var texts = MessageTexts.For(null);
        var payload = MessageRenderer.PushPayload(texts.Locale, Guid.CreateVersion7(), texts["title.test"], texts["test.push"], Evaluator.PublicUrl(configuration), null, null);
        var result = await channel.SendAsync(device, Encoding.UTF8.GetBytes(payload), "normal", null, cancellationToken);

        await using var scope = units.CreateScope();
        await channel.RecordAsync(scope.ServiceProvider.GetRequiredService<NotifyDbContext>(), [result], cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        return Results.Ok(new PushTestView(result.Outcome == PushDeviceOutcome.Sent, result.Status, result.Outcome == PushDeviceOutcome.Gone));
    }

    public static string DeviceLabel(string? userAgent)
    {
        var ua = userAgent ?? string.Empty;
        string? browser = ua switch
        {
            _ when ua.Contains("Edg/", StringComparison.Ordinal) || ua.Contains("EdgA/", StringComparison.Ordinal) || ua.Contains("EdgiOS/", StringComparison.Ordinal) => "Edge",
            _ when ua.Contains("OPR/", StringComparison.Ordinal) => "Opera",
            _ when ua.Contains("SamsungBrowser/", StringComparison.Ordinal) => "Samsung Internet",
            _ when ua.Contains("Firefox/", StringComparison.Ordinal) || ua.Contains("FxiOS/", StringComparison.Ordinal) => "Firefox",
            _ when ua.Contains("Chrome/", StringComparison.Ordinal) || ua.Contains("CriOS/", StringComparison.Ordinal) => "Chrome",
            _ when ua.Contains("Safari/", StringComparison.Ordinal) || ua.Contains("AppleWebKit/", StringComparison.Ordinal) => "Safari",
            _ => null,
        };
        string? system = ua switch
        {
            _ when ua.Contains("iPhone", StringComparison.Ordinal) => "iPhone",
            _ when ua.Contains("iPad", StringComparison.Ordinal) => "iPad",
            _ when ua.Contains("Android", StringComparison.Ordinal) => "Android",
            _ when ua.Contains("Mac OS X", StringComparison.Ordinal) || ua.Contains("Macintosh", StringComparison.Ordinal) => "macOS",
            _ when ua.Contains("Windows", StringComparison.Ordinal) => "Windows",
            _ when ua.Contains("CrOS", StringComparison.Ordinal) => "ChromeOS",
            _ when ua.Contains("Linux", StringComparison.Ordinal) => "Linux",
            _ => null,
        };
        return (browser, system) switch
        {
            ({ } b, { } s) => $"{b} on {s}",
            ({ } b, null) => b,
            (null, { } s) => $"Browser on {s}",
            _ => "Browser",
        };
    }

    private static IResult NotFound() => ApiProblems.Problem("push_subscription_not_found", StatusCodes.Status404NotFound, "No such push subscription.");
}
