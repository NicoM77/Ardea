using Ardea.Iam.Domain;
using Ardea.Iam.Persistence;
using Ardea.Platform.Audit;
using Ardea.Platform.Email;
using Ardea.Platform.Http;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Ardea.Iam.Features;

internal sealed record CreateOrganizationRequest(string? Name, string? Slug);

internal sealed record UpdateOrganizationRequest(string? Name, string? Locale, string? TimeZone, bool? RequireMfa);

internal sealed record OrganizationView(Guid Id, string Slug, string Name, string Locale, string TimeZone, bool RequireMfa, string Role, DateTimeOffset CreatedAt);

internal sealed record MemberView(Guid UserId, string DisplayName, string Email, string Role, DateTimeOffset JoinedAt);

internal sealed record UpdateMemberRequest(string? Role);

internal sealed record CreateInvitationRequest(string? Email, string? Role);

/// <summary>An invitation; <see cref="Link"/> only right after creating it on an instance without SMTP, to pass on by hand.</summary>
internal sealed record InvitationView(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt, bool Expired, string InvitedBy, DateTimeOffset CreatedAt, string? Link = null);

internal sealed record InvitationPreview(string OrganizationName, string Email, string Role, string InvitedBy, DateTimeOffset ExpiresAt);

internal sealed record AcceptInvitationResponse(Guid OrganizationId, string Slug, string Name);

/// <summary>
/// Organisations, members and invitations (master plan 9.1, 10.2; task 2.1). Every endpoint below
/// <c>/orgs/{orgId}</c> passes the platform's membership check first.
/// </summary>
internal static class OrganizationEndpoints
{
    /// <summary>Languages the web app and the e-mails speak.</summary>
    private static readonly string[] Locales = ["en", "de", "fr", "it"];

    /// <summary>Shown for an inviter who has left the organisation.</summary>
    internal const string FormerMember = "A former member";

    public static void Map(IEndpointRouteBuilder api)
    {
        var orgs = api.MapGroup("/orgs").WithTags("Organizations").RequireAuthorization();
        orgs.MapGet("/", ListAsync).WithName("listOrganizations");
        orgs.MapPost("/", CreateAsync).WithName("createOrganization").Produces<OrganizationView>(StatusCodes.Status201Created).Problems(400, 409);

        var org = api.MapOrganization().WithTags("Organizations");
        org.MapGet("/", GetAsync).WithName("getOrganization").Produces<OrganizationView>().Problems(403, 404);
        org.MapPatch("/", UpdateAsync).WithName("updateOrganization").RequireOrganizationRole(OrganizationRole.Admin)
            .Produces<OrganizationView>().Problems(400, 403, 404, 409);
        org.MapGet("/members", MembersAsync).WithName("listMembers").Produces<IReadOnlyList<MemberView>>().Problems(403, 404);
        org.MapPatch("/members/{userId:guid}", UpdateMemberAsync).WithName("updateMember").RequireOrganizationRole(OrganizationRole.Admin)
            .Produces<MemberView>().Problems(400, 403, 404, 409);
        org.MapDelete("/members/{userId:guid}", RemoveMemberAsync).WithName("removeMember").NoContent().Problems(403, 404, 409);
        org.MapGet("/invitations", InvitationsAsync).WithName("listInvitations").RequireOrganizationRole(OrganizationRole.Admin)
            .Produces<IReadOnlyList<InvitationView>>().Problems(403, 404);
        org.MapPost("/invitations", InviteAsync).WithName("createInvitation").RequireOrganizationRole(OrganizationRole.Admin)
            .Produces<InvitationView>(StatusCodes.Status201Created).Problems(400, 403, 404, 409);
        org.MapDelete("/invitations/{id:guid}", RevokeInvitationAsync).WithName("revokeInvitation").RequireOrganizationRole(OrganizationRole.Admin)
            .NoContent().Problems(403, 404);

        var invitations = api.MapGroup("/invitations").WithTags("Organizations");
        invitations.MapGet("/{token}", PreviewAsync).WithName("previewInvitation").Produces<InvitationPreview>().Problems(404)
            .RequireRateLimiting(AuthEndpoints.ReadRateLimitPolicy);
        invitations.MapPost("/{token}/accept", AcceptAsync).WithName("acceptInvitation").RequireAuthorization()
            .Produces<AcceptInvitationResponse>().Problems(403, 404).RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
    }

    private static async Task<IReadOnlyList<OrganizationSummary>> ListAsync(IamDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        await OrganizationDirectory.ForUserAsync(db, tenant.UserId!.Value, cancellationToken);

    /// <summary>Any signed-in user may found an organisation and becomes its owner.</summary>
    private static async Task<IResult> CreateAsync(
        CreateOrganizationRequest request, IamDbContext db, TenantContext tenant, IUnitOfWork unitOfWork, IAuditLog audit, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var slug = string.IsNullOrWhiteSpace(request.Slug) ? OrganizationDirectory.Slugify(name ?? string.Empty) : request.Slug.Trim().ToLowerInvariant();
        var errors = new FieldErrors()
            .Required("name", name, "Enter a name.")
            .MaxLength("name", name, 200)
            .Check("slug", OrganizationDirectory.IsValidSlug(slug), "Use lower-case letters, digits and hyphens, at most 50 characters.");
        if (errors.Any)
        {
            return IamErrors.Validation(errors.Errors);
        }

        // The new organisation is the tenant of this unit of work from its first statement on.
        var organizationId = Guid.CreateVersion7();
        var userId = tenant.UserId!.Value;
        tenant.SetOrganization(organizationId);
        var now = time.GetUtcNow();
        var organization = new Organization { OrganizationId = organizationId, Name = name!, Slug = slug, CreatedAt = now };
        db.Organizations.Add(organization);
        db.Memberships.Add(new Membership { Id = Guid.CreateVersion7(), OrganizationId = organizationId, UserId = userId, Role = "owner", CreatedAt = now });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_organization_slug" })
        {
            return IamErrors.Conflict("slug_taken", "This address is already taken. Choose another one.");
        }

        await audit.RecordAsync(new AuditEntry("org.created", "organization", organizationId.ToString(), new { name, slug }), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/orgs/{organizationId}", View(organization, "owner"));
    }

    private static async Task<IResult> GetAsync(HttpContext http, IamDbContext db, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.SingleAsync(cancellationToken);
        return Results.Ok(View(organization, http.OrganizationAccess().Role.ToApi()));
    }

    private static async Task<IResult> UpdateAsync(
        UpdateOrganizationRequest request, HttpContext http, IamDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var errors = new FieldErrors()
            .Check("name", request.Name is null || name!.Length > 0, "Enter a name.")
            .MaxLength("name", name, 200)
            .Check("locale", request.Locale is null || Locales.Contains(request.Locale), $"Choose one of {string.Join(", ", Locales)}.")
            .Check("timeZone", request.TimeZone is null || IsTimeZone(request.TimeZone), "Choose a time zone such as Europe/Zurich.");
        if (errors.Any)
        {
            return IamErrors.Validation(errors.Errors);
        }

        var access = http.OrganizationAccess();
        if (request.RequireMfa == true && !access.UserHasMfa)
        {
            // Whoever turns the requirement on must meet it, or they would lock themselves out.
            return IamErrors.Conflict("mfa_required_for_you", "Turn on two-factor authentication for your own account first.");
        }

        var organization = await db.Organizations.SingleAsync(cancellationToken);
        organization.Name = name ?? organization.Name;
        organization.Locale = request.Locale ?? organization.Locale;
        organization.TimeZone = request.TimeZone ?? organization.TimeZone;
        if (request.RequireMfa is { } requireMfa)
        {
            organization.Settings = organization.Settings with { RequireMfa = requireMfa };
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("org.settings_changed", "organization", organization.OrganizationId.ToString(), request), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(View(organization, access.Role.ToApi()));
    }

    /// <summary>The member directory: name, e-mail address and role, nothing else (master plan 9.2).</summary>
    private static async Task<IReadOnlyList<MemberView>> MembersAsync(IamDbContext db, CancellationToken cancellationToken)
    {
        var members = await db.Memberships
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new MemberView(u.Id, u.DisplayName, u.Email!, m.Role, m.CreatedAt))
            .ToListAsync(cancellationToken);
        return [.. members.OrderByDescending(m => OrganizationRoles.TryParse(m.Role, out var r) ? r : OrganizationRole.Viewer)
            .ThenBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// Changes a member's role. Setting <c>owner</c> transfers ownership: only the owner may, and
    /// becomes an administrator. The owner's own role changes only by such a transfer.
    /// </summary>
    private static async Task<IResult> UpdateMemberAsync(
        Guid userId, UpdateMemberRequest request, HttpContext http, IamDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, CancellationToken cancellationToken)
    {
        if (!OrganizationRoles.TryParse(request.Role, out var role))
        {
            return IamErrors.Validation(new() { ["role"] = ["Choose owner, admin, member or viewer."] });
        }

        var access = http.OrganizationAccess();
        var member = await db.Memberships.SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return IamErrors.NotFound("member_not_found", "No such member.");
        }

        var before = member.Role;
        if (role == OrganizationRole.Owner)
        {
            if (access.Role != OrganizationRole.Owner)
            {
                return IamErrors.Forbidden("insufficient_role", "Only the owner can transfer ownership.");
            }

            if (userId != access.UserId)
            {
                var self = await db.Memberships.SingleAsync(m => m.UserId == access.UserId, cancellationToken);
                // The old owner steps down first: the database allows one owner at a time.
                self.Role = "admin";
                await db.SaveChangesAsync(cancellationToken);
                member.Role = "owner";
                await db.SaveChangesAsync(cancellationToken);
                await audit.RecordAsync(new AuditEntry("org.ownership_transferred", "user", userId.ToString(), new { from = access.UserId }), cancellationToken);
            }
        }
        else
        {
            if (member.Role == "owner")
            {
                return IamErrors.Conflict("owner_role", "Transfer ownership to another member first.");
            }

            member.Role = role.ToApi();
            await db.SaveChangesAsync(cancellationToken);
            if (before != member.Role)
            {
                await audit.RecordAsync(new AuditEntry("org.member_role_changed", "user", userId.ToString(), new { from = before, to = member.Role }), cancellationToken);
            }
        }

        var user = await db.Users.Where(u => u.Id == userId).Select(u => new { u.DisplayName, u.Email }).SingleAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(new MemberView(userId, user.DisplayName, user.Email!, member.Role, member.CreatedAt));
    }

    /// <summary>Administrators remove members; every member may leave. The owner leaves only after a transfer.</summary>
    private static async Task<IResult> RemoveMemberAsync(
        Guid userId, HttpContext http, IamDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, CancellationToken cancellationToken)
    {
        var access = http.OrganizationAccess();
        if (userId != access.UserId && access.Role < OrganizationRole.Admin)
        {
            return IamErrors.Forbidden("insufficient_role", "Your role in this organisation does not allow this.");
        }

        var member = await db.Memberships.SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return IamErrors.NotFound("member_not_found", "No such member.");
        }

        if (member.Role == "owner")
        {
            return IamErrors.Conflict("owner_role", "Transfer ownership to another member first.");
        }

        db.Memberships.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry(userId == access.UserId ? "org.member_left" : "org.member_removed", "user", userId.ToString()), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IReadOnlyList<InvitationView>> InvitationsAsync(IamDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        // An inviter who has left is no longer visible to the organisation (row-level security).
        var rows = await db.Invitations
            .Select(i => new { Invitation = i, DisplayName = db.Users.Where(u => u.Id == i.InvitedBy).Select(u => u.DisplayName).FirstOrDefault() })
            .OrderByDescending(x => x.Invitation.CreatedAt)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(x => View(x.Invitation, x.DisplayName ?? FormerMember, now))];
    }

    /// <summary>
    /// Invites an address with a role. With SMTP the mail carries the link; without it the answer
    /// does, once, for the administrator to pass on. A new invitation for the same address replaces the open one.
    /// </summary>
    private static async Task<IResult> InviteAsync(
        CreateInvitationRequest request, HttpContext http, IamDbContext db, UserManager<ArdeaUser> users, IEmailSender email, IJobQueue jobs,
        IUnitOfWork unitOfWork, IAuditLog audit, IConfiguration configuration, TimeProvider time, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors().Email("email", request.Email);
        var validRole = OrganizationRoles.TryParse(request.Role, out var role) && role != OrganizationRole.Owner;
        errors.Check("role", validRole, "Choose admin, member or viewer.");
        if (errors.Any)
        {
            return IamErrors.Validation(errors.Errors);
        }

        var access = http.OrganizationAccess();
        var address = request.Email!.Trim();
        var normalized = users.NormalizeEmail(address);
        if (await db.Memberships.Join(db.Users, m => m.UserId, u => u.Id, (m, u) => u.NormalizedEmail).AnyAsync(e => e == normalized, cancellationToken))
        {
            return IamErrors.Conflict("already_member", "This person is already a member.");
        }

        await db.Invitations.Where(i => i.NormalizedEmail == normalized).ExecuteDeleteAsync(cancellationToken);
        var token = OrganizationDirectory.NewToken();
        var now = time.GetUtcNow();
        var invitation = new Invitation
        {
            Id = Guid.CreateVersion7(),
            Email = address,
            NormalizedEmail = normalized,
            Role = role.ToApi(),
            TokenHash = OrganizationDirectory.HashToken(token),
            ExpiresAt = now + Invitation.Lifetime,
            InvitedBy = access.UserId,
            CreatedAt = now,
        };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);

        string? link = null;
        if (await email.IsConfiguredAsync(cancellationToken))
        {
            await jobs.EnqueueAsync(new SendInvitationEmail(invitation.Id), cancellationToken: cancellationToken);
        }
        else
        {
            link = InvitationLink(configuration, token).AbsoluteUri;
        }

        await audit.RecordAsync(new AuditEntry("org.member_invited", "invitation", invitation.Id.ToString(), new { email = address, role = invitation.Role }), cancellationToken);
        var inviter = await db.Users.Where(u => u.Id == access.UserId).Select(u => u.DisplayName).SingleAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/orgs/{access.OrganizationId}/invitations/{invitation.Id}", View(invitation, inviter, now) with { Link = link });
    }

    private static async Task<IResult> RevokeInvitationAsync(Guid id, IamDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, CancellationToken cancellationToken)
    {
        var deleted = await db.Invitations.Where(i => i.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            return IamErrors.NotFound("invitation_not_found", "No such invitation.");
        }

        await audit.RecordAsync(new AuditEntry("org.invitation_revoked", "invitation", id.ToString()), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>What the invitation page shows before sign-in or registration; the token is the authorisation.</summary>
    private static async Task<IResult> PreviewAsync(
        string token, NpgsqlDataSource dataSource, TenantContext tenant, IamDbContext db, CancellationToken cancellationToken)
    {
        if (await OrganizationDirectory.OrganizationOfTokenAsync(dataSource, token, null, cancellationToken) is not { } organizationId)
        {
            return IamErrors.NotFound("invalid_invitation", "The invitation is not valid or has expired.");
        }

        tenant.SetOrganization(organizationId);
        var hash = OrganizationDirectory.HashToken(token);
        var preview = await db.Invitations.Where(i => i.TokenHash == hash)
            .Select(i => new { i.Email, i.Role, i.ExpiresAt, Inviter = db.Users.Where(u => u.Id == i.InvitedBy).Select(u => u.DisplayName).FirstOrDefault() })
            .SingleOrDefaultAsync(cancellationToken);
        var name = await db.Organizations.Select(o => o.Name).SingleAsync(cancellationToken);
        return preview is null
            ? IamErrors.NotFound("invalid_invitation", "The invitation is not valid or has expired.")
            : Results.Ok(new InvitationPreview(name, preview.Email, preview.Role, preview.Inviter ?? FormerMember, preview.ExpiresAt));
    }

    /// <summary>The signed-in user joins with the invited role; the invitation is for their address only.</summary>
    private static async Task<IResult> AcceptAsync(
        string token, NpgsqlDataSource dataSource, TenantContext tenant, IamDbContext db, IUnitOfWork unitOfWork,
        IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        if (await OrganizationDirectory.OrganizationOfTokenAsync(dataSource, token, null, cancellationToken) is not { } organizationId)
        {
            return IamErrors.NotFound("invalid_invitation", "The invitation is not valid or has expired.");
        }

        var userId = tenant.UserId!.Value;
        tenant.SetOrganization(organizationId);
        var hash = OrganizationDirectory.HashToken(token);
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        if (invitation is null || invitation.ExpiresAt <= time.GetUtcNow())
        {
            return IamErrors.NotFound("invalid_invitation", "The invitation is not valid or has expired.");
        }

        if (!string.Equals(invitation.NormalizedEmail, user.NormalizedEmail, StringComparison.Ordinal))
        {
            return IamErrors.Forbidden("invitation_email_mismatch", "This invitation is for another e-mail address. Sign in with that address.");
        }

        if (!await db.Memberships.AnyAsync(m => m.UserId == userId, cancellationToken))
        {
            db.Memberships.Add(new Membership { Id = Guid.CreateVersion7(), UserId = userId, Role = invitation.Role, CreatedAt = time.GetUtcNow() });
            await audit.RecordAsync(new AuditEntry("org.member_joined", "user", userId.ToString(), new { role = invitation.Role, invitation = invitation.Id }), cancellationToken);
        }

        db.Invitations.Remove(invitation);
        if (!user.EmailConfirmed)
        {
            // The link came to this address, so the address is confirmed.
            user.EmailConfirmed = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        var organization = await db.Organizations.SingleAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(new AcceptInvitationResponse(organization.OrganizationId, organization.Slug, organization.Name));
    }

    internal static Uri InvitationLink(IConfiguration configuration, string token)
    {
        var url = configuration["PUBLIC_URL"] ?? throw new InvalidOperationException("ARDEA_PUBLIC_URL is not set.");
        return new Uri(new Uri(url.EndsWith('/') ? url : url + "/"), "invitations/" + token);
    }

    private static OrganizationView View(Organization o, string role) =>
        new(o.OrganizationId, o.Slug, o.Name, o.Locale, o.TimeZone, o.Settings.RequireMfa, role, o.CreatedAt);

    private static InvitationView View(Invitation i, string inviter, DateTimeOffset now) =>
        new(i.Id, i.Email, i.Role, i.ExpiresAt, i.ExpiresAt <= now, inviter, i.CreatedAt);

    private static bool IsTimeZone(string id) => TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
}
