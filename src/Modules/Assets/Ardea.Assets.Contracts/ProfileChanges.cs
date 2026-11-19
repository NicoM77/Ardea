namespace Ardea.Assets.Contracts;

/// <summary>
/// Told when a project's profile changed: components added, changed or removed, or the project's
/// exposure or criticality (master plan 3.3, phase 2 acceptance). The Assessment module subscribes
/// to start its reverse search for the project; Assets knows nobody.
/// </summary>
public interface IProfileChangeSubscriber
{
    Task OnProfileChangedAsync(ProfileChanged change, CancellationToken cancellationToken);
}

/// <param name="OrganizationId">The organisation of the project.</param>
/// <param name="ProjectId">The project whose profile changed.</param>
public sealed record ProfileChanged(Guid OrganizationId, Guid ProjectId);
