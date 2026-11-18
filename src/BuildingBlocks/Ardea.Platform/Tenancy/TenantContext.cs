namespace Ardea.Platform.Tenancy;

/// <summary>
/// Who is acting and for which organisation (master plan 3.5, 9.2). Per request it comes from the
/// route and the checked membership, per job from the job's <c>org_id</c>. Without an organisation,
/// organisation data cannot be read.
/// </summary>
public interface ITenantContext
{
    Guid? OrganizationId { get; }

    Guid? UserId { get; }

    /// <summary>The organisation, or an exception when there is none: for code that must not run without one.</summary>
    Guid RequiredOrganizationId => OrganizationId
        ?? throw new MissingTenantContextException();
}

/// <summary>The scoped, settable tenant context. Set once per request or job, before any data access.</summary>
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

    /// <summary>Called when the unit of work's transaction begins with this tenant.</summary>
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
