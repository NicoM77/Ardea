namespace Ardea.Platform.Tenancy;

public interface ITenantContext
{
    Guid? OrganizationId { get; }

    Guid? UserId { get; }

    Guid RequiredOrganizationId => OrganizationId
        ?? throw new MissingTenantContextException();
}

public sealed class TenantContext : ITenantContext
{
    private bool _frozen;

    public Guid? OrganizationId { get; private set; }

    public Guid? UserId { get; private set; }

    public void SetOrganization(Guid organizationId)
    {
        ThrowIfFrozen(OrganizationId == organizationId);
        if (OrganizationId is { } current && current != organizationId)
        {
            throw new InvalidOperationException("The organisation of a unit of work cannot change.");
        }

        OrganizationId = organizationId;
    }

    public void SetUser(Guid userId)
    {
        ThrowIfFrozen(UserId == userId);
        if (UserId is { } current && current != userId)
        {
            throw new InvalidOperationException("The user of a unit of work cannot change.");
        }

        UserId = userId;
    }

    internal void Freeze() => _frozen = true;

    private void ThrowIfFrozen(bool unchanged)
    {
        if (_frozen && !unchanged)
        {
            throw new InvalidOperationException("The tenant context cannot change once the unit of work's transaction has begun.");
        }
    }
}

public sealed class MissingTenantContextException()
    : InvalidOperationException("Organisation data was accessed without an organisation context.");
