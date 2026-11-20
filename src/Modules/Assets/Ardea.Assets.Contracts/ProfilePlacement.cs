namespace Ardea.Assets.Contracts;

/// <summary>
/// Records where a technology runs, for an answer «affects us» to a notice (master plan 17.3, ADR 047): in a project of the
/// current organisation, the component of the product another component names, with the version a person gave. Runs in the
/// caller's unit of work, audits the change and queues the project's reverse search as the profile endpoints do. The one
/// write contract between modules: a person's statement about the profile, made on another module's page (ADR 047).
/// </summary>
public interface IProfilePlacement
{
    /// <summary>
    /// The component of <paramref name="likeComponentId"/>'s product in <paramref name="projectId"/>, found or recorded:
    /// <list type="number">
    /// <item>without a version, the given component itself when it lies in the project, else a component of the product
    /// there, one without an exact version first; with a version, one whose version is that version;</item>
    /// <item>with a version, else a hand-entered component of the product without an exact version, which takes it; a
    /// component of the product a repository reads is not changed and nothing is added next to it (master plan 16.5);</item>
    /// <item>else a new hand-entered component of the product, with the version if one was given.</item>
    /// </list>
    /// Calls for the same project and product take turns.
    /// </summary>
    Task<PlacementResult> PlaceAsync(Guid projectId, Guid likeComponentId, string? version, CancellationToken cancellationToken);
}

/// <summary>What became of a placement.</summary>
/// <param name="Outcome"><c>found</c>, <c>version_set</c> or <c>added</c>; or why nothing was placed: <c>not_found</c> (project
/// or component), <c>archived</c> (the project), <c>repository</c> (a repository reads the product there; its version belongs to it).</param>
/// <param name="ComponentId">The component placed; for <c>repository</c> the repository's component; else <c>null</c>.</param>
public sealed record PlacementResult(string Outcome, Guid? ComponentId = null)
{
    public bool Placed => Outcome is "found" or "version_set" or "added";
}
