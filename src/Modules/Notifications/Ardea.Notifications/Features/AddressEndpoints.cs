using System.Security.Cryptography;
using System.Text;
using Ardea.Iam.Contracts;
using Ardea.Notifications.Delivering;
using Ardea.Notifications.Evaluation;
using Ardea.Notifications.Messages;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Audit;
using Ardea.Platform.Email;
using Ardea.Platform.Http;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Secrets;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Notifications.Features;

/// <summary>
/// External e-mail addresses (master plan 8.3, 8.5; task 5.1): an admin adds one, the address's owner confirms
/// it with a link from a mail, and only then does it receive messages. Every message carries a signed link that
/// unsubscribes it without signing in. Confirming and unsubscribing know no session: the token is the proof.
/// </summary>
internal static class AddressEndpoints
{
    public static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromDays(7);

    public static void Map(IEndpointRouteBuilder api)
    {
        var addresses = api.MapOrganization().WithTags("Notifications").MapGroup("/external-addresses").RequireOrganizationRole(OrganizationRole.Admin);
        addresses.MapGet("/", ListAsync).WithName("listExternalAddresses").Produces<IReadOnlyList<ExternalAddressView>>().Problems(403, 404);
        addresses.MapPost("/", AddAsync).WithName("addExternalAddress").Produces<ExternalAddressView>(StatusCodes.Status201Created).Problems(400, 403, 404, 409);
        addresses.MapPost("/{id:guid}/resend", ResendAsync).WithName("resendAddressConfirmation").Produces<ExternalAddressView>().Problems(403, 404, 409);
        addresses.MapDelete("/{id:guid}", DeleteAsync).WithName("deleteExternalAddress").NoContent().Problems(403, 404, 409);

        var links = api.MapGroup("/notify/addresses").WithTags("Notifications");
        links.MapGet("/preview", PreviewAsync).WithName("previewAddressLink").AllowAnonymous().Produces<AddressSubscriptionView>().Problems(404);
        links.MapPost("/confirm", ConfirmAsync).WithName("confirmExternalAddress").AuthenticatedByToken().NoContent().Problems(404);
        links.MapPost("/unsubscribe", UnsubscribeAsync).WithName("unsubscribeExternalAddress").AuthenticatedByToken().NoContent().Problems(404);
    }

    private static async Task<IReadOnlyList<ExternalAddressView>> ListAsync(NotifyDbContext db, TimeProvider time, CancellationToken cancellationToken) =>
        [.. (await db.Addresses.AsNoTracking().OrderBy(a => a.NormalizedAddress).ToListAsync(cancellationToken)).Select(a => View(a, time.GetUtcNow()))];

    private static async Task<IResult> AddAsync(
        ExternalAddressRequest request, NotifyDbContext db, IEmailSender email, IJobQueue jobs, ITenantContext tenant, IAuditLog audit, IUnitOfWork unitOfWork,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var address = request.Address?.Trim();
        if (string.IsNullOrEmpty(address) || address.Length > 320 || !System.Net.Mail.MailAddress.TryCreate(address, out var parsed) || parsed.Address != address)
        {
            return ApiProblems.Validation("invalid_address", "The address is not valid.", new Dictionary<string, string[]> { ["address"] = ["Enter a valid e-mail address."] });
        }

        if (!await email.IsConfiguredAsync(cancellationToken))
        {
            return ApiProblems.Problem("smtp_not_configured", StatusCodes.Status409Conflict, "E-mail is off on this instance: the address could not be confirmed.");
        }

        var normalized = address.ToUpperInvariant();
        if (await db.Addresses.AnyAsync(a => a.NormalizedAddress == normalized, cancellationToken))
        {
            return ApiProblems.Problem("address_exists", StatusCodes.Status409Conflict, "This address is already on the list.");
        }

        var now = time.GetUtcNow();
        var entry = new ExternalAddress { Id = Guid.CreateVersion7(), Address = address, NormalizedAddress = normalized, CreatedAt = now };
        db.Addresses.Add(entry);
        await RequestConfirmationAsync(entry, db, jobs, tenant, now, cancellationToken);
        await audit.RecordAsync(new AuditEntry("notify.address_added", "address", entry.Id.ToString(), new { entry.Address }), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/orgs/{tenant.RequiredOrganizationId}/external-addresses/{entry.Id}", View(entry, now));
    }

    private static async Task<IResult> ResendAsync(
        Guid id, NotifyDbContext db, IJobQueue jobs, ITenantContext tenant, IUnitOfWork unitOfWork, TimeProvider time, CancellationToken cancellationToken)
    {
        var entry = await db.Addresses.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (entry is null)
        {
            return NotFound();
        }

        if (entry.ConfirmedAt is not null && entry.UnsubscribedAt is null)
        {
            return ApiProblems.Problem("address_confirmed", StatusCodes.Status409Conflict, "This address is already confirmed.");
        }

        // A recipient who unsubscribed can opt in again only through a new confirmation.
        entry.ConfirmedAt = null;
        entry.UnsubscribedAt = null;
        var now = time.GetUtcNow();
        await RequestConfirmationAsync(entry, db, jobs, tenant, now, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(View(entry, now));
    }

    private static async Task<IResult> DeleteAsync(Guid id, NotifyDbContext db, IAuditLog audit, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        var entry = await db.Addresses.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (entry is null)
        {
            return NotFound();
        }

        var reference = id.ToString();
        if (await db.Targets.AnyAsync(t => t.RecipientKind == RecipientKinds.Address && t.RecipientRef == reference, cancellationToken))
        {
            return ApiProblems.Problem("address_in_use", StatusCodes.Status409Conflict, "A rule still sends to this address. Remove it from the rule first.");
        }

        db.Addresses.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("notify.address_deleted", "address", entry.Id.ToString(), new { entry.Address }), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task RequestConfirmationAsync(ExternalAddress entry, NotifyDbContext db, IJobQueue jobs, ITenantContext tenant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The job makes the token: a job carries ids only, never a secret (master plan 4.1).
        entry.TokenHash = null;
        entry.TokenExpiresAt = now + ConfirmationLifetime;
        await db.SaveChangesAsync(cancellationToken);
        await jobs.EnqueueAsync(new SendAddressConfirmation(entry.Id), new EnqueueOptions { CoalesceKey = $"notify.confirm-address:{entry.Id}" }, cancellationToken);
    }

    /// <summary>Shows the organisation and the address behind a confirmation or unsubscribe link.</summary>
    private static async Task<IResult> PreviewAsync(string? token, IServiceProvider services, ISecretProtector secrets, CancellationToken cancellationToken)
    {
        if (Resolve(token, secrets) is not { } link)
        {
            return NotFound();
        }

        return await InOrganizationAsync(services, link.Organization, async (db, profiles, time) =>
        {
            var entry = await FindAsync(db, link, time, cancellationToken);
            var profile = await profiles.FindAsync(link.Organization, cancellationToken);
            return entry is null || profile is null
                ? NotFound()
                : Results.Ok(new AddressSubscriptionView(profile.Name, entry.Address, View(entry, time.GetUtcNow()).State));
        }, commit: false, cancellationToken);
    }

    private static async Task<IResult> ConfirmAsync(string? token, IServiceProvider services, ISecretProtector secrets, CancellationToken cancellationToken)
    {
        if (Resolve(token, secrets) is not { Confirmation: true } link)
        {
            return NotFound();
        }

        return await InOrganizationAsync(services, link.Organization, async (db, _, time) =>
        {
            var entry = await FindAsync(db, link, time, cancellationToken);
            if (entry is null)
            {
                return NotFound();
            }

            entry.ConfirmedAt = time.GetUtcNow();
            entry.UnsubscribedAt = null;
            entry.TokenHash = null;
            entry.TokenExpiresAt = null;
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }, commit: true, cancellationToken);
    }

    /// <summary>The unsubscribe link of every message to an external address; mail clients post here for one-click (RFC 8058).</summary>
    private static async Task<IResult> UnsubscribeAsync(string? token, IServiceProvider services, ISecretProtector secrets, CancellationToken cancellationToken)
    {
        if (Resolve(token, secrets) is not { Confirmation: false } link)
        {
            return NotFound();
        }

        return await InOrganizationAsync(services, link.Organization, async (db, _, time) =>
        {
            var entry = await FindAsync(db, link, time, cancellationToken);
            if (entry is null)
            {
                return NotFound();
            }

            entry.UnsubscribedAt ??= time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }, commit: true, cancellationToken);
    }

    /// <summary>
    /// The link names its organisation; the unit of work runs as that tenant, as the invitation does (master plan 9.2).
    /// A confirmation token proves itself against its stored hash, an unsubscribe token by its signature.
    /// </summary>
    private static async Task<IResult> InOrganizationAsync(
        IServiceProvider services, Guid organization, Func<NotifyDbContext, IOrganizationProfiles, TimeProvider, Task<IResult>> work, bool commit, CancellationToken cancellationToken)
    {
        await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetOrganization(organization);
        var result = await work(
            scope.ServiceProvider.GetRequiredService<NotifyDbContext>(), scope.ServiceProvider.GetRequiredService<IOrganizationProfiles>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        if (commit && result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status204NoContent })
        {
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        }

        return result;
    }

    private static async Task<ExternalAddress?> FindAsync(NotifyDbContext db, LinkToken link, TimeProvider time, CancellationToken cancellationToken)
    {
        var entry = await db.Addresses.SingleOrDefaultAsync(a => a.Id == link.Address, cancellationToken);
        if (entry is null || !link.Confirmation)
        {
            return entry;
        }

        return entry.TokenHash is { } hash && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(Hash(link.Token)))
            && entry.TokenExpiresAt > time.GetUtcNow()
            ? entry
            : null;
    }

    /// <summary>A confirmation token: organisation and address in the clear, then 32 random bytes; only its hash is stored.</summary>
    internal static string ConfirmationToken(Guid organization, Guid address) =>
        $"c.{organization:N}.{address:N}.{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}";

    private static LinkToken? Resolve(string? token, ISecretProtector secrets)
    {
        token = token?.Trim();
        if (string.IsNullOrEmpty(token) || token.Length > 1000)
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts is ["c", var organization, var address, _] && Guid.TryParseExact(organization, "N", out var o) && Guid.TryParseExact(address, "N", out var a))
        {
            return new LinkToken(o, a, Confirmation: true, token);
        }

        return DeliveryReader.ReadUnsubscribeToken(secrets, token) is { } unsubscribe
            ? new LinkToken(unsubscribe.Organization, unsubscribe.Address, Confirmation: false, token)
            : null;
    }

    internal static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static ExternalAddressView View(ExternalAddress a, DateTimeOffset now) => new(
        a.Id, a.Address,
        a.UnsubscribedAt is not null ? "unsubscribed" : a.ConfirmedAt is not null ? "confirmed" : a.TokenExpiresAt is { } expires && expires <= now ? "expired" : "pending",
        a.ConfirmedAt, a.CreatedAt);

    private static IResult NotFound() => ApiProblems.Problem("address_not_found", StatusCodes.Status404NotFound, "No such address, or the link is no longer valid.");

    private sealed record LinkToken(Guid Organization, Guid Address, bool Confirmation, string Token);
}

/// <summary>Sends the confirmation link to a new external address (master plan 8.3).</summary>
internal sealed record SendAddressConfirmation(Guid AddressId) : IJob
{
    public static string Kind => "notify.send-address-confirmation";
}

/// <summary>
/// Makes a fresh token and stores its hash in a short unit of work, then mails without an open transaction
/// (master plan 9.2). A retry makes another token; only the latest link works.
/// </summary>
internal sealed class SendAddressConfirmationHandler(IUnitOfWorkFactory units, IEmailSender email, IConfiguration configuration, ITenantContext tenant)
    : IJobHandler<SendAddressConfirmation>
{
    public async Task HandleAsync(SendAddressConfirmation job, JobContext context, CancellationToken cancellationToken)
    {
        var organization = tenant.RequiredOrganizationId;
        var token = AddressEndpoints.ConfirmationToken(organization, job.AddressId);
        string? address;
        OrganizationProfile? profile;
        await using (var scope = units.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotifyDbContext>();
            var entry = await db.Addresses.SingleOrDefaultAsync(a => a.Id == job.AddressId && a.ConfirmedAt == null && a.TokenExpiresAt != null, cancellationToken);
            profile = await scope.ServiceProvider.GetRequiredService<IOrganizationProfiles>().FindAsync(organization, cancellationToken);
            if (entry is null || profile is null)
            {
                return;
            }

            entry.TokenHash = AddressEndpoints.Hash(token);
            address = entry.Address;
            await db.SaveChangesAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        }

        var texts = MessageTexts.For(profile.Locale);
        var link = new Uri(Evaluator.PublicUrl(configuration), $"notifications/confirm?token={Uri.EscapeDataString(token)}");
        await email.SendAsync(EmailTemplate.Render(
            address,
            texts.Format("confirm.subject", profile.Name),
            texts["confirm.greeting"],
            [texts.Format("confirm.body", profile.Name), texts["confirm.ignore"]],
            texts["confirm.action"],
            link,
            texts.Format("confirm.footer", profile.Name)), cancellationToken);
    }
}
