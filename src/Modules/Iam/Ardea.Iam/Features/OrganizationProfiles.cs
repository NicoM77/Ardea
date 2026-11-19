using Ardea.Iam.Contracts;
using Ardea.Iam.Persistence;
using Ardea.Platform.Ai;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Iam.Features;

/// <summary>An organisation's language and AI data policy for other modules, in the caller's unit of work.</summary>
internal sealed class OrganizationProfiles(IamDbContext db) : IOrganizationProfiles
{
    public async Task<OrganizationProfile?> FindAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.OrganizationId == organizationId, cancellationToken);
        return organization is null
            ? null
            : new OrganizationProfile(organization.OrganizationId, organization.Locale, organization.Settings.AiPolicy ?? AiDataPolicies.Default)
            {
                Name = organization.Name,
                Slug = organization.Slug,
                TimeZone = organization.TimeZone,
                EmailContent = organization.Settings.EmailContent ?? "full",
                AllowMuteCritical = organization.Settings.AllowMuteCritical,
            };
    }
}

/// <summary>The member directory for other modules, in the caller's unit of work.</summary>
internal sealed class OrganizationMembers(IamDbContext db) : IOrganizationMembers
{
    public async Task<IReadOnlyList<OrganizationMember>> ListAsync(CancellationToken cancellationToken) =>
        await db.Memberships
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new OrganizationMember(u.Id, u.DisplayName, u.Email!, m.Role))
            .ToListAsync(cancellationToken);
}
