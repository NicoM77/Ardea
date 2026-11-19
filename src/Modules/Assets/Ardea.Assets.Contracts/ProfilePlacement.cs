namespace Ardea.Assets.Contracts;

/// <summary>
/// Records where a technology runs, for an answer «affects us» to a notice (master plan 17.3, ADR 047): in a project of the
/// current organisation, the component of the product another component names, with the version a person gave. Runs in the
/// caller's unit of work, audits the change and queues the project's reverse search as the profile endpoints do.
/// </summary>
public interface IProfilePlacement
{
    /// <summary>
    /// The component of <paramref name="likeComponentId"/>'s product in <paramref name="projectId"/>, found or recorded:
    /// <list type="number">
    /// <item>without a version, the given component itself when it lies in the project, else a component of the product
    /// there, one without an exact version first; with a version, one whose version is that version;</item>
    /// <item>with a version, else a hand-entered component of the product without an exact version, which takes it;</item>
    /// <item>else a new hand-entered component of the product, with the version if one was given.</item>
    /// </list>
    /// </summary>
    /// <returns>The component, or <c>null</c> when the project or the given component does not exist or the project is archived.</returns>
    Task<PlacedComponent?> PlaceAsync(Guid projectId, Guid likeComponentId, string? version, CancellationToken cancellationToken);
}

/// <param name="ComponentId">The component.</param>
/// <param name="Change"><c>none</c>, <c>version_set</c> or <c>added</c>.</param>
public sealed record PlacedComponent(Guid ComponentId, string Change);
