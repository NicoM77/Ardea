namespace Ardea.Iam.Contracts;

/// <summary>
/// The member directory of the current organisation (master plan 9.2): name, e-mail address and role, nothing
/// else. The notifications resolve the recipients of a rule through it, inside the organisation's job.
/// </summary>
public interface IOrganizationMembers
{
    Task<IReadOnlyList<OrganizationMember>> ListAsync(CancellationToken cancellationToken);
}

/// <param name="UserId">The account.</param>
/// <param name="DisplayName">The name shown to others.</param>
/// <param name="Email">The account's e-mail address.</param>
/// <param name="Role"><c>owner</c>, <c>admin</c>, <c>member</c> or <c>viewer</c>.</param>
public sealed record OrganizationMember(Guid UserId, string DisplayName, string Email, string Role);

/// <summary>
/// Told when an organisation was created, inside the unit of work that creates it: whatever a module sets up
/// for a new organisation commits with it. The Notifications module creates the default rules (master plan 8.3).
/// </summary>
public interface IOrganizationCreatedSubscriber
{
    Task OnOrganizationCreatedAsync(Guid organizationId, CancellationToken cancellationToken);
}

/// <summary>
/// Told when an organisation's time zone changed, inside the unit of work that changes it. The Notifications
/// module moves the next digests to the times of the new zone (master plan 8.3).
/// </summary>
public interface IOrganizationTimeZoneSubscriber
{
    Task OnTimeZoneChangedAsync(Guid organizationId, string timeZone, CancellationToken cancellationToken);
}
