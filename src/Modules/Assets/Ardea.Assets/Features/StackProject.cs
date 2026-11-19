using Ardea.Assets.Persistence;
using Ardea.Iam.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assets.Features;

/// <summary>
/// Every organisation has exactly one stack from its creation on (master plan 17.2, ADR 046): a system project of the
/// kind <c>organization_stack</c>, without components until the wizard asks for them (task 5a.4). It is created in the
/// unit of work that creates the organisation; the migration <c>StackAndPrecision</c> created it for those before.
/// </summary>
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

    /// <summary>
    /// The module's master-data step, after every <c>migrate</c> (ADR 046): every organisation without a stack gets one,
    /// whether it existed before task 5a.2 or the previous API created it during the upgrade, when the hook above did not
    /// exist yet. Idempotent; reads only the organisations' ids from <c>iam.organization</c>, which migrates before Assets
    /// (master plan 4.11). A test database without the Iam schema has no organisations.
    /// </summary>
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
