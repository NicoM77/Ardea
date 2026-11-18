namespace Ardea.Platform.Tenancy;

/// <summary>The role of a member in one organisation (master plan 9.1); a higher role includes the lower ones.</summary>
public enum OrganizationRole
{
    Viewer = 0,
    Member = 1,
    Admin = 2,
    Owner = 3,
}

/// <summary>What the signed-in user may do in one organisation, as the membership check found it.</summary>
public sealed record OrganizationAccess(Guid OrganizationId, Guid UserId, OrganizationRole Role, bool MfaRequired, bool UserHasMfa);

/// <summary>
/// Looks up a user's membership in an organisation for the access check in front of every
/// organisation endpoint (master plan 9.2, layer 1). Implemented by <c>Iam</c>; it runs inside the
/// request's unit of work after the organisation is set.
/// </summary>
public interface IOrganizationMembership
{
    Task<OrganizationAccess?> FindAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);
}

public static class OrganizationRoles
{
    public static string ToApi(this OrganizationRole role) => role switch
    {
        OrganizationRole.Owner => "owner",
        OrganizationRole.Admin => "admin",
        OrganizationRole.Member => "member",
        _ => "viewer",
    };

    public static bool TryParse(string? value, out OrganizationRole role)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "owner": role = OrganizationRole.Owner; return true;
            case "admin": role = OrganizationRole.Admin; return true;
            case "member": role = OrganizationRole.Member; return true;
            case "viewer": role = OrganizationRole.Viewer; return true;
            default: role = OrganizationRole.Viewer; return false;
        }
    }
}
