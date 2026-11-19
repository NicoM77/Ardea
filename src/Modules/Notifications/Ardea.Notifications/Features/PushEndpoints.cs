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

/// <summary>The instance's VAPID public key, which a browser subscribes with.</summary>
/// <param name="PublicKey">The uncompressed P-256 point, base64url.</param>
internal sealed record PushPublicKeyView(string PublicKey);

/// <summary>A device of the signed-in user that receives Web Push.</summary>
/// <param name="Id">The subscription.</param>
/// <param name="Label">The browser and system, e.g. «Chrome on macOS».</param>
/// <param name="CreatedAt">When the device subscribed.</param>
/// <param name="LastSuccessAt">When a push service last accepted a message for it.</param>
/// <param name="FailedCount">Failed attempts since the last success.</param>
internal sealed record PushSubscriptionView(Guid Id, string Label, DateTimeOffset CreatedAt, DateTimeOffset? LastSuccessAt, int FailedCount);

/// <summary>The browser's subscription as <c>PushSubscription.toJSON()</c> writes it.</summary>
internal sealed record PushSubscriptionRequest(string? Endpoint, PushSubscriptionKeysRequest? Keys, double? ExpirationTime);

/// <param name="P256dh">The browser's P-256 key, base64url.</param>
/// <param name="Auth">The browser's 16-byte authentication secret, base64url.</param>
internal sealed record PushSubscriptionKeysRequest(string? P256dh, string? Auth);

/// <summary>The outcome of a test message to one device.</summary>
/// <param name="Ok">The push service accepted it.</param>
/// <param name="Status">The push service's HTTP status, or <c>blocked</c>, <c>unreachable</c>, <c>timeout</c>.</param>
/// <param name="Removed">The subscription had expired and was removed.</param>
internal sealed record PushTestView(bool Ok, string? Status, bool Removed);

/// <summary>
/// Web Push subscriptions of the signed-in user (master plan 8.5, 10.2; task 5.5, ADR 040). A device belongs to
/// the user, not to an organisation: it receives the push messages of every organisation they are a member of,
/// each with its own mute. Only endpoints at the browser makers' push services are accepted (master plan 9.5).
/// </summary>
internal static class PushEndpoints
{
    /// <summary>Devices per user; the oldest goes when a new one subscribes beyond it.</summary>
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
        me.MapPost("/{id:guid}/test", TestAsync).WithName("testPushSubscription").Produces<PushTestView>().Problems(404);
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
        var subscription = await db.PushSubscriptions.SingleOrDefaultAsync(s => s.UserId == user && s.EndpointHash == hash, cancellationToken);
        var created = subscription is null;
        if (subscription is null)
        {
            subscription = new PushSubscription
            {
                Id = Guid.CreateVersion7(),
                UserId = user,
                Endpoint = endpoint,
                EndpointHash = hash,
                P256dh = string.Empty,
                Auth = string.Empty,
                Label = string.Empty,
                CreatedAt = now,
            };
            db.PushSubscriptions.Add(subscription);
            var others = await db.PushSubscriptions.Where(s => s.UserId == user).OrderByDescending(s => s.CreatedAt).Skip(DevicesPerUser - 1).ToListAsync(cancellationToken);
            db.PushSubscriptions.RemoveRange(others);
        }

        subscription.P256dh = request.Keys!.P256dh!.Trim();
        subscription.Auth = request.Keys.Auth!.Trim();
        subscription.UserAgent = userAgent.Length > 500 ? userAgent[..500] : userAgent;
        subscription.Label = DeviceLabel(userAgent);
        subscription.FailedCount = 0;
        await db.SaveChangesAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        var view = new PushSubscriptionView(subscription.Id, subscription.Label, subscription.CreatedAt, subscription.LastSuccessAt, subscription.FailedCount);
        return created ? Results.Created($"/api/v1/me/push-subscriptions/{subscription.Id}", view) : Results.Ok(view);
    }

    /// <summary>This device turns push off: the browser names its endpoint; nothing to find is fine.</summary>
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

    /// <summary>A test message to one device now. The request holds no transaction while it waits for the push service (master plan 9.2).</summary>
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

    /// <summary>«Chrome on macOS», «Safari on iPhone»: enough to tell one's devices apart; «Browser» when unknown.</summary>
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
