namespace Ardea.Assets.Contracts;

public interface IProfileChangeSubscriber
{
    Task OnProfileChangedAsync(ProfileChanged change, CancellationToken cancellationToken);
}

public sealed record ProfileChanged(Guid OrganizationId, Guid ProjectId);
