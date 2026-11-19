namespace Ardea.Iam.Contracts;

/// <summary>
/// The instance administrators who can be reached by e-mail: accounts with the instance role and a confirmed
/// address (master plan 12.7, ADR 043). The self-check tells them when the instance warns about itself; it runs
/// without an organisation and without a user, and reads nothing else of the accounts.
/// </summary>
public interface IInstanceAdministrators
{
    Task<IReadOnlyList<InstanceAdministrator>> ListReachableAsync(CancellationToken cancellationToken);
}

/// <param name="UserId">The account.</param>
/// <param name="DisplayName">The name for the greeting.</param>
/// <param name="Email">The account's confirmed e-mail address.</param>
public sealed record InstanceAdministrator(Guid UserId, string DisplayName, string Email);
