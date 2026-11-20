namespace Ardea.Assets.Contracts;

public interface IProfileQueries
{
    Task<IReadOnlyList<Guid>> OrganizationsWithKeysAsync(IReadOnlyCollection<ProfileKey> keys, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProfileKeyHit>> FindByKeysAsync(IReadOnlyCollection<ProfileKey> keys, CancellationToken cancellationToken);

    Task<ProjectProfile?> FindProjectAsync(Guid projectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProfileComponent>> FindComponentsAsync(IReadOnlyCollection<Guid> componentIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> SearchComponentsAsync(string text, int limit, CancellationToken cancellationToken);
}

public sealed record ProfileKey(string Type, string Value);

public sealed record ProfileKeyHit(Guid ProjectId, Guid ComponentId, string Type, string Value, string Strength);

public sealed record ProjectProfile(
    Guid Id, string Name, string Criticality, string Exposure, bool Archived, DateTimeOffset CreatedAt, IReadOnlyList<ProfileComponent> Components);

public sealed record ProfileComponent
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required bool ProjectArchived { get; init; }

    public required string ProjectName { get; init; }

    public string ProjectKind { get; init; } = "project";

    public string? ProductKey { get; init; }

    public string? CustomVendor { get; init; }

    public string? CustomName { get; init; }

    public required string Name { get; init; }

    public required string Kind { get; init; }

    public string? Version { get; init; }

    public string VersionPrecision { get; init; } = "exact";

    public string NoticeThreshold { get; init; } = "only_important";

    public string? VersionScheme { get; init; }

    public required string Packaging { get; init; }

    public string? Purl { get; init; }

    public required string Exposure { get; init; }

    public required string Criticality { get; init; }

    public required string Protection { get; init; }

    public string? ProtectionNote { get; init; }

    public required string Origin { get; init; }

    public Guid? RepositoryLinkId { get; init; }

    public string? DependencyScope { get; init; }

    public IReadOnlyList<ProfileKey> StrongKeys { get; init; } = [];

    public IReadOnlyList<ProfileKey> WeakKeys { get; init; } = [];
}
