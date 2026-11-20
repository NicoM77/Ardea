namespace Ardea.Iam.Contracts;

public interface IOrganizationMembers
{
    Task<IReadOnlyList<OrganizationMember>> ListAsync(CancellationToken cancellationToken);
}

public sealed record OrganizationMember(Guid UserId, string DisplayName, string Email, string Role);

public interface IOrganizationCreatedSubscriber
{
    Task OnOrganizationCreatedAsync(Guid organizationId, CancellationToken cancellationToken);
}

public interface IOrganizationTimeZoneSubscriber
{
    Task OnTimeZoneChangedAsync(Guid organizationId, string timeZone, CancellationToken cancellationToken);
}
