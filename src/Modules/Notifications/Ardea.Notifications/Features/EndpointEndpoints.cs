using Ardea.Notifications.Delivering;
using Ardea.Notifications.Messages;
using Ardea.Notifications.Persistence;
using Ardea.Platform.Audit;
using Ardea.Platform.Http;
using Ardea.Platform.Persistence;
using Ardea.Platform.Secrets;
using Ardea.Platform.Settings;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Notifications.Features;

internal static class EndpointEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var endpoints = api.MapOrganization().WithTags("Notifications").MapGroup("/endpoints").RequireOrganizationRole(OrganizationRole.Admin);
        endpoints.MapGet("/", ListAsync).WithName("listWebhookEndpoints").Produces<IReadOnlyList<WebhookEndpointView>>().Problems(403, 404);
        endpoints.MapPost("/", CreateAsync).WithName("createWebhookEndpoint").RequireRateLimiting(RateLimits.Outbound).Produces<WebhookEndpointView>(StatusCodes.Status201Created).Problems(400, 403, 404);
        endpoints.MapPut("/{id:guid}", UpdateAsync).WithName("updateWebhookEndpoint").RequireRateLimiting(RateLimits.Outbound).Produces<WebhookEndpointView>().Problems(400, 403, 404);
        endpoints.MapDelete("/{id:guid}", DeleteAsync).WithName("deleteWebhookEndpoint").NoContent().Problems(403, 404, 409);
        endpoints.MapPost("/{id:guid}/test", TestAsync).WithName("testWebhookEndpoint").RequireRateLimiting(RateLimits.Outbound).Produces<WebhookTestView>().Problems(403, 404);
    }

    private static async Task<IReadOnlyList<WebhookEndpointView>> ListAsync(NotifyDbContext db, ISecretProtector secrets, CancellationToken cancellationToken) =>
        [.. (await db.Endpoints.AsNoTracking().OrderBy(e => e.Name).ToListAsync(cancellationToken)).Select(e => View(e, secrets))];

    private static async Task<IResult> CreateAsync(
        WebhookEndpointRequest request, NotifyDbContext db, ISecretProtector secrets, IInstanceSettings settings, ITenantContext tenant, IAuditLog audit,
        IUnitOfWork unitOfWork, TimeProvider time, CancellationToken cancellationToken)
    {
        var (url, errors) = await CheckAsync(request, settings, required: true, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
        {
            errors["name"] = ["Enter a name of at most 200 characters."];
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation("invalid_endpoint", "The webhook target is not valid.", errors);
        }

        var secret = WebhookChannel.NewSecret();
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name!.Trim(),
            UrlEnc = secrets.Protect(DeliveryReader.UrlPurpose, url!.AbsoluteUri),
            SecretEnc = secrets.Protect(DeliveryReader.SecretPurpose, secret),
            Enabled = request.Enabled ?? true,
            CreatedAt = time.GetUtcNow(),
        };
        db.Endpoints.Add(endpoint);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("notify.endpoint_created", "endpoint", endpoint.Id.ToString(), new { endpoint.Name, host = url.Host }), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/orgs/{tenant.RequiredOrganizationId}/endpoints/{endpoint.Id}", View(endpoint, secrets) with { Secret = secret });
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, WebhookEndpointRequest request, NotifyDbContext db, ISecretProtector secrets, IInstanceSettings settings, IAuditLog audit,
        IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        var endpoint = await db.Endpoints.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (endpoint is null)
        {
            return NotFound();
        }

        var (url, errors) = await CheckAsync(request, settings, required: false, cancellationToken);
        if (request.Name is not null && (request.Name.Trim().Length is 0 or > 200))
        {
            errors["name"] = ["Enter a name of at most 200 characters."];
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation("invalid_endpoint", "The webhook target is not valid.", errors);
        }

        endpoint.Name = request.Name?.Trim() ?? endpoint.Name;
        endpoint.Enabled = request.Enabled ?? endpoint.Enabled;
        if (url is not null)
        {
            endpoint.UrlEnc = secrets.Protect(DeliveryReader.UrlPurpose, url.AbsoluteUri);
        }

        string? secret = null;
        if (request.RotateSecret == true)
        {
            secret = WebhookChannel.NewSecret();
            endpoint.SecretEnc = secrets.Protect(DeliveryReader.SecretPurpose, secret);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditEntry("notify.endpoint_changed", "endpoint", endpoint.Id.ToString(), new { endpoint.Name, endpoint.Enabled, urlChanged = url is not null, secretRotated = secret is not null }),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(View(endpoint, secrets) with { Secret = secret });
    }

    private static async Task<IResult> DeleteAsync(Guid id, NotifyDbContext db, IAuditLog audit, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        var endpoint = await db.Endpoints.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (endpoint is null)
        {
            return NotFound();
        }

        var reference = id.ToString();
        if (await db.Targets.AnyAsync(t => t.RecipientKind == Rules.RecipientKinds.Endpoint && t.RecipientRef == reference, cancellationToken))
        {
            return ApiProblems.Problem("endpoint_in_use", StatusCodes.Status409Conflict, "A rule still sends to this endpoint. Remove it from the rule first.");
        }

        db.Endpoints.Remove(endpoint);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("notify.endpoint_deleted", "endpoint", endpoint.Id.ToString(), new { endpoint.Name }), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> TestAsync(
        Guid id, NotifyDbContext db, ISecretProtector secrets, IInstanceSettings settings, IUnitOfWork unitOfWork, IUnitOfWorkFactory units,
        WebhookChannel channel, ITenantContext tenant, Iam.Contracts.IOrganizationProfiles organizations, TimeProvider time, CancellationToken cancellationToken)
    {
        var endpoint = await db.Endpoints.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (endpoint is null)
        {
            return NotFound();
        }

        var url = secrets.Unprotect(DeliveryReader.UrlPurpose, endpoint.UrlEnc) is { } text ? new Uri(text) : null;
        var secret = secrets.Unprotect(DeliveryReader.SecretPurpose, endpoint.SecretEnc);
        var released = InstanceSettingDefinitions.Hosts((await settings.GetAsync(InstanceSettingDefinitions.WebhookInternalHosts, cancellationToken)).Value);
        var organization = await organizations.FindAsync(tenant.RequiredOrganizationId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        if (url is null || secret is null || organization is null)
        {
            return Results.Ok(new WebhookTestView(false, null, "The endpoint's address or secret cannot be read."));
        }

        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            schema = "ardea.notification.v1",
            id = Guid.CreateVersion7(),
            kind = "test",
            organization = new { id = organization.Id, slug = organization.Slug, name = organization.Name },
            events = Array.Empty<object>(),
            message = MessageTexts.For(organization.Locale)["test.body"],
        }, MessageRenderer.Json);
        var result = await channel.PostAsync(url, secret, body, Guid.CreateVersion7(), WebhookChannel.IsReleased(url, released), cancellationToken);

        await using var scope = units.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<NotifyDbContext>();
        if (await store.Endpoints.SingleOrDefaultAsync(e => e.Id == id, cancellationToken) is { } stored)
        {
            stored.LastStatus = result.Status ?? result.Outcome.ToString().ToLowerInvariant();
            stored.LastStatusAt = time.GetUtcNow();
            await store.SaveChangesAsync(cancellationToken);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        return Results.Ok(new WebhookTestView(result.Outcome == SendOutcome.Sent, result.Status, result.Error));
    }

    private static async Task<(Uri? Url, Dictionary<string, string[]> Errors)> CheckAsync(
        WebhookEndpointRequest request, IInstanceSettings settings, bool required, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.Url is null && !required)
        {
            return (null, errors);
        }

        if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out var url) || url.AbsoluteUri.Length > 2000)
        {
            errors["url"] = ["Enter the address the messages are posted to, e.g. https://hooks.example.com/ardea."];
            return (null, errors);
        }

        var released = InstanceSettingDefinitions.Hosts((await settings.GetAsync(InstanceSettingDefinitions.WebhookInternalHosts, cancellationToken)).Value);
        if (await WebhookChannel.CheckAddressAsync(url, released, cancellationToken) is { } error)
        {
            errors["url"] = [error];
        }

        return (url, errors);
    }

    private static WebhookEndpointView View(WebhookEndpoint e, ISecretProtector secrets) => new(
        e.Id, e.Name, secrets.Unprotect(DeliveryReader.UrlPurpose, e.UrlEnc) is { } url && Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.Host : string.Empty,
        e.Enabled, e.LastStatus, e.LastStatusAt, e.CreatedAt);

    private static IResult NotFound() => ApiProblems.Problem("endpoint_not_found", StatusCodes.Status404NotFound, "No such webhook target.");
}
