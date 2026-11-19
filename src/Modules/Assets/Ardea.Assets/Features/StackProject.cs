using Ardea.Assets.Persistence;
using Ardea.Iam.Contracts;

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
}
