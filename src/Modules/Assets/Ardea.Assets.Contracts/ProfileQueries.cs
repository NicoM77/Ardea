namespace Ardea.Assets.Contracts;

/// <summary>
/// Read access to the profiles for the Assessment module (master plan 3.2, 7.2, 7.3). Everything
/// except <see cref="OrganizationsWithKeysAsync"/> runs in the tenant context of the unit of work and
/// sees only that organisation's projects; archived projects take no part in the matching.
/// </summary>
public interface IProfileQueries
{
    /// <summary>
    /// System operation «candidate search» (master plan 9.2): which organisations have a component
    /// under one of these keys. Returns organisation ids only and needs no tenant context; the
    /// assessment itself runs as a job in each organisation.
    /// </summary>
    Task<IReadOnlyList<Guid>> OrganizationsWithKeysAsync(IReadOnlyCollection<ProfileKey> keys, CancellationToken cancellationToken);

    /// <summary>The components of the current organisation that carry one of the keys, with the key and its strength.</summary>
    Task<IReadOnlyList<ProfileKeyHit>> FindByKeysAsync(IReadOnlyCollection<ProfileKey> keys, CancellationToken cancellationToken);

    /// <summary>A project of the current organisation with its components and their keys; <c>null</c> when it does not exist.</summary>
    Task<ProjectProfile?> FindProjectAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>Components of the current organisation with their context; components that no longer exist are left out.</summary>
    Task<IReadOnlyList<ProfileComponent>> FindComponentsAsync(IReadOnlyCollection<Guid> componentIds, CancellationToken cancellationToken);

    /// <summary>
    /// Components of the current organisation whose name, product key, package or project name contains
    /// the text, ignoring case; for the search in the actions list (master plan 10.2). At most <paramref name="limit"/> ids.
    /// </summary>
    Task<IReadOnlyList<Guid>> SearchComponentsAsync(string text, int limit, CancellationToken cancellationToken);
}

/// <summary>A search key of <c>assets.component_key</c>.</summary>
/// <param name="Type"><c>product</c>, <c>cpe</c> (<c>vendor:product</c>) or <c>purl</c> (<c>pkg:type/name</c>).</param>
/// <param name="Value">The key.</param>
public sealed record ProfileKey(string Type, string Value);

/// <summary>A component found under a key; <c>Strength</c> is <c>strong</c> or <c>weak</c> (master plan 7.2).</summary>
public sealed record ProfileKeyHit(Guid ProjectId, Guid ComponentId, string Type, string Value, string Strength);

/// <summary>A project with what the assessment needs (master plan 4.5, 7.3); an archived project has left the matching.</summary>
public sealed record ProjectProfile(
    Guid Id, string Name, string Criticality, string Exposure, bool Archived, DateTimeOffset CreatedAt, IReadOnlyList<ProfileComponent> Components);

/// <summary>A component with its context; exposure and criticality already fall back to the project's.</summary>
public sealed record ProfileComponent
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    /// <summary>The project is archived: the component takes no part in the matching.</summary>
    public required bool ProjectArchived { get; init; }

    public required string ProjectName { get; init; }

    /// <summary><c>project</c>, or <c>organization_stack</c> for the organisation's stack (master plan 17.2).</summary>
    public string ProjectKind { get; init; } = "project";

    public string? ProductKey { get; init; }

    public string? CustomVendor { get; init; }

    public string? CustomName { get; init; }

    /// <summary>The product's name for display: the catalogue's, the custom name or the package.</summary>
    public required string Name { get; init; }

    public required string Kind { get; init; }

    public string? Version { get; init; }

    /// <summary>
    /// <c>none</c>, <c>line</c> or <c>exact</c> (master plan 17.1, ADR 046): <c>exact</c> takes the precise path (finding and
    /// reminder), <c>none</c> and <c>line</c> the coarse one (notice).
    /// </summary>
    public string VersionPrecision { get; init; } = "exact";

    /// <summary><c>only_important</c> or <c>everything</c>: when a coarse component raises a notice (ADR 044, ADR 046).</summary>
    public string NoticeThreshold { get; init; } = "only_important";

    public string? VersionScheme { get; init; }

    /// <summary><c>upstream</c>, <c>distribution</c> or <c>unknown</c>.</summary>
    public required string Packaging { get; init; }

    public string? Purl { get; init; }

    /// <summary><c>internet</c>, <c>internal</c> or <c>isolated</c>; the component's own or the project's.</summary>
    public required string Exposure { get; init; }

    /// <summary><c>high</c>, <c>normal</c> or <c>low</c>; the component's own or the project's.</summary>
    public required string Criticality { get; init; }

    /// <summary><c>none</c> or <c>effective</c>.</summary>
    public required string Protection { get; init; }

    public string? ProtectionNote { get; init; }

    /// <summary><c>manual</c> or <c>repository</c>.</summary>
    public required string Origin { get; init; }

    public Guid? RepositoryLinkId { get; init; }

    /// <summary><c>runtime</c>, <c>development</c> (master plan 16.5), <c>unknown</c> or <c>null</c> for manual components.</summary>
    public string? DependencyScope { get; init; }

    public IReadOnlyList<ProfileKey> StrongKeys { get; init; } = [];

    public IReadOnlyList<ProfileKey> WeakKeys { get; init; } = [];
}
