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

internal static class AddressEndpoints
{
    public static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromDays(7);

    internal static class Limits
    {
        public const int AddressesPerOrganization = 50;

        public const int ConfirmationsPerAddress = 3;

        public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);

        public const int ConfirmationsPerOrganizationPerDay = 20;

        public const int OrganizationsPerAddressPerDay = 3;
    }

    public static void Map(IEndpointRouteBuilder api)
    {
        var addresses = api.MapOrganization().WithTags("Notifications").MapGroup("/external-addresses").RequireOrganizationRole(OrganizationRole.Admin);
        addresses.MapGet("/", ListAsync).WithName("listExternalAddresses").Produces<IReadOnlyList<ExternalAddressView>>().Problems(403, 404);
        addresses.MapPost("/", AddAsync).WithName("addExternalAddress").RequireRateLimiting(RateLimits.Outbound).Produces<ExternalAddressView>(StatusCodes.Status201Created).Problems(400, 403, 404, 409);
        addresses.MapPost("/{id:guid}/resend", ResendAsync).WithName("resendAddressConfirmation").RequireRateLimiting(RateLimits.Outbound).Produces<ExternalAddressView>().Problems(403, 404, 409);
        addresses.MapDelete("/{id:guid}", DeleteAsync).WithName("deleteExternalAddress").NoContent().Problems(403, 404, 409);

        var links = api.MapGroup("/notify/addresses").WithTags("Notifications").RequireRateLimiting(RateLimits.Lookup);
        links.MapPost("/preview", PreviewAsync).WithName("previewAddressLink").AuthenticatedByToken().Produces<AddressSubscriptionView>().Problems(404);
        links.MapPost("/confirm", ConfirmAsync).WithName("confirmExternalAddress").AuthenticatedByToken().NoContent().Problems(404);
        links.MapPost("/unsubscribe", (AddressLinkRequest request, IServiceProvider services, ISecretProtector secrets, CancellationToken cancellationToken) =>
                UnsubscribeAsync(request.Token, services, secrets, cancellationToken))
            .WithName("unsubscribeExternalAddress").AuthenticatedByToken().NoContent().Problems(404);
        links.MapPost("/unsubscribe/one-click", UnsubscribeAsync).WithName("unsubscribeExternalAddressOneClick").AuthenticatedByToken().NoContent().Problems(404);
        links.MapPost("/resubscribe", ResubscribeAsync).WithName("resubscribeExternalAddress").AuthenticatedByToken().NoContent().Problems(404);
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

        if (await db.Addresses.CountAsync(cancellationToken) >= Limits.AddressesPerOrganization)
        {
            return ApiProblems.Problem("address_limit", StatusCodes.Status409Conflict, $"An organisation can have at most {Limits.AddressesPerOrganization} external addresses.");
        }

        var now = time.GetUtcNow();
        var entry = new ExternalAddress { Id = Guid.CreateVersion7(), Address = address, NormalizedAddress = normalized, CreatedAt = now };
        if (await LimitedAsync(entry, db, now, cancellationToken) is { } limited)
        {
            return limited;
        }

        db.Addresses.Add(entry);
        await RequestConfirmationAsync(entry, db, jobs, now, cancellationToken);
        await audit.RecordAsync(new AuditEntry("notify.address_added", "address", entry.Id.ToString(), new { entry.Address }), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/orgs/{tenant.RequiredOrganizationId}/external-addresses/{entry.Id}", View(entry, now));
    }

    private static async Task<IResult> ResendAsync(
        Guid id, NotifyDbContext db, IJobQueue jobs, IUnitOfWork unitOfWork, TimeProvider time, CancellationToken cancellationToken)
    {
        var entry = await db.Addresses.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (entry is null)
        {
            return NotFound();
        }

        if (entry.UnsubscribedAt is not null)
        {
            return Unsubscribed();
        }

        if (entry.ConfirmedAt is not null)
        {
            return ApiProblems.Problem("address_confirmed", StatusCodes.Status409Conflict, "This address is already confirmed.");
        }

        var now = time.GetUtcNow();
        if (await LimitedAsync(entry, db, now, cancellationToken) is { } limited)
        {
            return limited;
        }

        await RequestConfirmationAsync(entry, db, jobs, now, cancellationToken);
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

        if (entry.UnsubscribedAt is not null)
        {
            return Unsubscribed();
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

    private static async Task<IResult?> LimitedAsync(ExternalAddress entry, NotifyDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (entry.ConfirmationsSent >= Limits.ConfirmationsPerAddress)
        {
            return TooMany($"This address was asked {Limits.ConfirmationsPerAddress} times already. Ask the recipient to look for the mail, or to contact you.");
        }

        if (entry.ConfirmationSentAt is { } last && now - last < Limits.Cooldown)
        {
            return TooMany($"A confirmation mail went to this address less than {Limits.Cooldown.TotalMinutes:0} minutes ago.");
        }

        var dayAgo = now - TimeSpan.FromDays(1);
        if (await db.Addresses.CountAsync(a => a.ConfirmationSentAt > dayAgo, cancellationToken) >= Limits.ConfirmationsPerOrganizationPerDay)
        {
            return TooMany($"This organisation asked for {Limits.ConfirmationsPerOrganizationPerDay} confirmations within 24 hours. Try again tomorrow.");
        }

        var askedBy = (await db.Database.SqlQuery<long>(
            $"SELECT notify.address_confirmations_since({entry.NormalizedAddress}, {dayAgo}) AS \"Value\"").ToListAsync(cancellationToken))[0];
        return askedBy >= Limits.OrganizationsPerAddressPerDay
            ? TooMany("This address was asked for confirmation too often today. Try again tomorrow.")
            : null;
    }

    private static IResult TooMany(string title) => ApiProblems.Problem("confirmation_limit", StatusCodes.Status429TooManyRequests, title);

    private static IResult Unsubscribed() => ApiProblems.Problem(
        "address_unsubscribed", StatusCodes.Status409Conflict, "The recipient unsubscribed. Only they can subscribe again, with the link in their last message.");

    private static async Task RequestConfirmationAsync(ExternalAddress entry, NotifyDbContext db, IJobQueue jobs, DateTimeOffset now, CancellationToken cancellationToken)
    {
        entry.TokenHash = null;
        entry.TokenExpiresAt = now + ConfirmationLifetime;
        entry.ConfirmationSentAt = now;
        entry.ConfirmationsSent++;
        await db.SaveChangesAsync(cancellationToken);
        await jobs.EnqueueAsync(new SendAddressConfirmation(entry.Id), new EnqueueOptions { CoalesceKey = $"notify.confirm-address:{entry.Id}" }, cancellationToken);
    }

    private static async Task<IResult> PreviewAsync(AddressLinkRequest request, IServiceProvider services, ISecretProtector secrets, CancellationToken cancellationToken)
    {
        if (Resolve(request.Token, secrets) is not { } link)
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

    private static async Task<IResult> ConfirmAsync(AddressLinkRequest request, IServiceProvider services, ISecretProtector secrets, CancellationToken cancellationToken)
    {
        if (Resolve(request.Token, secrets) is not { Confirmation: true } link)
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

    private static async Task<IResult> ResubscribeAsync(AddressLinkRequest request, IServiceProvider services, ISecretProtector secrets, CancellationToken cancellationToken)
    {
        if (Resolve(request.Token, secrets) is not { Confirmation: false } link)
        {
            return NotFound();
        }

        return await InOrganizationAsync(services, link.Organization, async (db, _, _) =>
        {
            var entry = await db.Addresses.SingleOrDefaultAsync(a => a.Id == link.Address && a.ConfirmedAt != null, cancellationToken);
            if (entry is null)
            {
                return NotFound();
            }

            entry.UnsubscribedAt = null;
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }, commit: true, cancellationToken);
    }

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

internal sealed record SendAddressConfirmation(Guid AddressId) : IJob
{
    public static string Kind => "notify.send-address-confirmation";
}

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
            var entry = await db.Addresses.SingleOrDefaultAsync(a => a.Id == job.AddressId && a.ConfirmedAt == null && a.UnsubscribedAt == null && a.TokenExpiresAt != null, cancellationToken);
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
        var link = new Uri(Evaluator.PublicUrl(configuration), $"notifications/confirm#token={Uri.EscapeDataString(token)}");
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
