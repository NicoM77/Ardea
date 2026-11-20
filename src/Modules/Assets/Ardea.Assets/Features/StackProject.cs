using Ardea.Assets.Persistence;
using Ardea.Iam.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assets.Features;

internal sealed class StackProject(AssetsDbContext db, TimeProvider time) : IOrganizationCreatedSubscriber
{
    public async Task OnOrganizationCreatedAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        db.Projects.Add(new Project
        {
            Id = Guid.CreateVersion7(),
            Kind = Project.StackKind,
            Name = Project.StackName,
            Description = "The technologies the organisation runs, without projects.",
            CreatedAt = now,
            ChangedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task<int> EnsureForEveryOrganizationAsync(AssetsDbContext db, CancellationToken cancellationToken)
    {
        var iam = await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('iam.organization') IS NOT NULL AS \"Value\"").SingleAsync(cancellationToken);
        if (!iam)
        {
            return 0;
        }

        return await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO assets.project (organization_id, kind, name, description, criticality, exposure, created_at, changed_at)
            SELECT o.id, 'organization_stack', 'Stack', 'The technologies the organisation runs, without projects.', 'normal', 'internal', now(), now()
            FROM iam.organization o
            WHERE NOT EXISTS (SELECT 1 FROM assets.project p WHERE p.organization_id = o.id AND p.kind = 'organization_stack')
            ON CONFLICT DO NOTHING
            """,
            cancellationToken);
    }
}
