namespace Ardea.Platform.Tenancy;

public enum OrganizationRole
{
    Viewer = 0,
    Member = 1,
    Admin = 2,
    Owner = 3,
}

public sealed record OrganizationAccess(Guid OrganizationId, Guid UserId, OrganizationRole Role, bool MfaRequired, bool UserHasMfa);

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
