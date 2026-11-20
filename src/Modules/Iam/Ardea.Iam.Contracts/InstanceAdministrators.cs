namespace Ardea.Iam.Contracts;

public interface IInstanceAdministrators
{
    Task<IReadOnlyList<InstanceAdministrator>> ListReachableAsync(CancellationToken cancellationToken);
}

public sealed record InstanceAdministrator(Guid UserId, string DisplayName, string Email);
