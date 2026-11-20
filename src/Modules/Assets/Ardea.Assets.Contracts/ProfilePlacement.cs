namespace Ardea.Assets.Contracts;

public interface IProfilePlacement
{
    Task<PlacementResult> PlaceAsync(Guid projectId, Guid likeComponentId, string? version, CancellationToken cancellationToken);
}

public sealed record PlacementResult(string Outcome, Guid? ComponentId = null);
