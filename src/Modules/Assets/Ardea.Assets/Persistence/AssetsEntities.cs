using System.Text.Json;
using Ardea.Platform.Persistence;

namespace Ardea.Assets.Persistence;

internal sealed class Project : IOrganizationOwned
{
    public const string ProjectKind = "project";
    public const string StackKind = "organization_stack";

    public const string StackName = "Stack";

    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public string Kind { get; set; } = ProjectKind;

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string Criticality { get; set; } = "normal";

    public string Exposure { get; set; } = "internal";

    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

internal sealed class Component : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ProjectId { get; set; }

    public string? ProductKey { get; set; }

    public string? CustomVendor { get; set; }

    public string? CustomName { get; set; }

    public required string Kind { get; set; }

    public string? Version { get; set; }

    public string VersionPrecision { get; set; } = VersionPrecisions.None;

    public string NoticeThreshold { get; set; } = NoticeThresholds.OnlyImportant;

    public string? VersionScheme { get; set; }

    public string Packaging { get; set; } = "unknown";

    public Guid? ParentComponentId { get; set; }

    public string? Exposure { get; set; }

    public string? Criticality { get; set; }

    public string Protection { get; set; } = "none";

    public string? ProtectionNote { get; set; }

    public string Origin { get; set; } = "manual";

    public Guid? RepositoryLinkId { get; set; }

    public string? Purl { get; set; }

    public string? DependencyScope { get; set; }

    public string? DependencyRelation { get; set; }

    public JsonDocument? Evidence { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

internal sealed class ComponentKey : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ComponentId { get; set; }

    public required string KeyType { get; set; }

    public required string KeyValue { get; set; }

    public required string Strength { get; set; }
}

internal sealed class RepositoryConnection : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Name { get; set; }

    public string? HostId { get; set; }

    public string? TokenEnc { get; set; }

    public string? TokenHint { get; set; }

    public string TokenKind { get; set; } = "none";

    public string[] TokenScopes { get; set; } = [];

    public DateTimeOffset? TokenExpiresAt { get; set; }

    public string? Login { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastCheckedAt { get; set; }

    public DateTimeOffset? LastOkAt { get; set; }

    public string? LastError { get; set; }
}

internal sealed class RepositoryLink : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ConnectionId { get; set; }

    public required string Owner { get; set; }

    public required string Name { get; set; }

    public string? Ref { get; set; }

    public string? Path { get; set; }

    public string State { get; set; } = "pending";

    public string? StateReason { get; set; }

    public DateTimeOffset? FailingSince { get; set; }

    public string? LastCommitSha { get; set; }

    public DateTimeOffset? LastIndexedAt { get; set; }

    public DateTimeOffset NextCheckAt { get; set; }

    public string? HeadEtag { get; set; }

    public byte[]? LastSbom { get; set; }

    public byte[]? PendingShrink { get; set; }

    public string? PendingShrinkCommit { get; set; }

    public DateTimeOffset? UnavailableRaisedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class ComponentSuggestion : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid RepositoryLinkId { get; set; }

    public required string ProductKey { get; set; }

    public string? Version { get; set; }

    public required JsonDocument Evidence { get; set; }

    public required string Fingerprint { get; set; }

    public string State { get; set; } = "open";

    public Guid? ComponentId { get; set; }

    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
