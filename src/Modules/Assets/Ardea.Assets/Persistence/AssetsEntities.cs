using System.Text.Json;
using Ardea.Platform.Persistence;

namespace Ardea.Assets.Persistence;

/// <summary>A project or system of an organisation (master plan 4.5); the setup wizard creates the first.</summary>
internal sealed class Project : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Name { get; set; }

    /// <summary>Also names the business process the project carries (master plan 4.5).</summary>
    public string? Description { get; set; }

    /// <summary><c>low</c>, <c>normal</c> or <c>high</c>.</summary>
    public string Criticality { get; set; } = "normal";

    /// <summary><c>internet</c>, <c>internal</c> or <c>isolated</c>.</summary>
    public string Exposure { get; set; } = "internal";

    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>A technology a project uses (master plan 4.5, 4.9, 16.5).</summary>
internal sealed class Component : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ProjectId { get; set; }

    /// <summary>The catalogue's key, or <c>null</c> for a product the catalogue does not know.</summary>
    public string? ProductKey { get; set; }

    public string? CustomVendor { get; set; }

    public string? CustomName { get; set; }

    /// <summary>One of the kinds of <c>catalog.product</c>: os, runtime, framework, …, service, other.</summary>
    public required string Kind { get; set; }

    public string? Version { get; set; }

    public string? VersionScheme { get; set; }

    /// <summary><c>upstream</c>, <c>distribution</c> (packaged by an OS distribution) or <c>unknown</c>.</summary>
    public string Packaging { get; set; } = "unknown";

    public Guid? ParentComponentId { get; set; }

    /// <summary>Overrides the project's exposure for this component; <c>null</c> inherits.</summary>
    public string? Exposure { get; set; }

    /// <summary>Overrides the project's criticality for this component; <c>null</c> inherits.</summary>
    public string? Criticality { get; set; }

    /// <summary><c>none</c> or <c>effective</c>: a protective measure that makes the weakness unreachable.</summary>
    public string Protection { get; set; } = "none";

    public string? ProtectionNote { get; set; }

    /// <summary><c>manual</c> or <c>repository</c>; a repository's components belong to their link (16.5).</summary>
    public string Origin { get; set; } = "manual";

    public Guid? RepositoryLinkId { get; set; }

    public string? Purl { get; set; }

    /// <summary><c>runtime</c>, <c>development</c> or <c>unknown</c>; repository components only.</summary>
    public string? DependencyScope { get; set; }

    /// <summary><c>direct</c>, <c>transitive</c> or <c>unknown</c>; repository components only.</summary>
    public string? DependencyRelation { get; set; }

    /// <summary>File, line and value a repository component was read from.</summary>
    public JsonDocument? Evidence { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>
/// The search index of the candidate search (master plan 4.5, 7.2): every key under which a component
/// can be hit by an entry, with the strength of such a hit. Written with the component.
/// </summary>
internal sealed class ComponentKey : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ComponentId { get; set; }

    /// <summary><c>cpe</c> (<c>vendor:product</c>), <c>product</c> (catalogue or raw key) or <c>purl</c> (<c>pkg:type/name</c>).</summary>
    public required string KeyType { get; set; }

    public required string KeyValue { get; set; }

    /// <summary><c>strong</c> or <c>weak</c> (master plan 7.2).</summary>
    public required string Strength { get; set; }
}

/// <summary>
/// An organisation's access to GitHub (master plan 4.5, 16.4): a fine-grained, read-only token for
/// selected repositories, or none for public repositories. The token is stored encrypted and never
/// shown again; only its last four characters and its expiry are.
/// </summary>
internal sealed class RepositoryConnection : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Name { get; set; }

    /// <summary>A registered GitHub Enterprise Server; <c>null</c> for github.com.</summary>
    public string? HostId { get; set; }

    /// <summary>The token, encrypted with the instance's master key; <c>null</c> for public repositories only.</summary>
    public string? TokenEnc { get; set; }

    public string? TokenHint { get; set; }

    /// <summary><c>none</c>, <c>fine_grained</c>, <c>classic</c> or <c>other</c>.</summary>
    public string TokenKind { get; set; } = "none";

    /// <summary>The scopes of a classic token, shown as a warning about what it can do.</summary>
    public string[] TokenScopes { get; set; } = [];

    public DateTimeOffset? TokenExpiresAt { get; set; }

    /// <summary>The GitHub account the token belongs to.</summary>
    public string? Login { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastCheckedAt { get; set; }

    public DateTimeOffset? LastOkAt { get; set; }

    public string? LastError { get; set; }
}

/// <summary>A repository that feeds a project (master plan 4.5, 16.2, 16.6).</summary>
internal sealed class RepositoryLink : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ConnectionId { get; set; }

    public required string Owner { get; set; }

    public required string Name { get; set; }

    /// <summary>The branch, tag or commit; <c>null</c> for the default branch.</summary>
    public string? Ref { get; set; }

    /// <summary>A subfolder of a monorepo that alone counts; <c>null</c> for the whole repository.</summary>
    public string? Path { get; set; }

    /// <summary><c>pending</c> (never read), <c>ok</c>, <c>partial</c> (no SBOM, own readers only) or <c>failing</c>.</summary>
    public string State { get; set; } = "pending";

    public string? StateReason { get; set; }

    public DateTimeOffset? FailingSince { get; set; }

    public string? LastCommitSha { get; set; }

    public DateTimeOffset? LastIndexedAt { get; set; }

    public DateTimeOffset NextCheckAt { get; set; }

    public string? HeadEtag { get; set; }

    /// <summary>The last SBOM, gzip-compressed (master plan 4.10: usually under 200 kB).</summary>
    public byte[]? LastSbom { get; set; }

    /// <summary>A much smaller state held back for confirmation (16.5), gzip-compressed JSON of the findings.</summary>
    public byte[]? PendingShrink { get; set; }

    public string? PendingShrinkCommit { get; set; }

    /// <summary>When <c>repository.unavailable</c> was raised for the current failure, so it is raised once.</summary>
    public DateTimeOffset? UnavailableRaisedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A platform a detection rule suggests for a project (master plan 4.5, 16.3, 16.5).</summary>
internal sealed class ComponentSuggestion : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid RepositoryLinkId { get; set; }

    public required string ProductKey { get; set; }

    /// <summary>The version line read, e.g. <c>10.0</c>.</summary>
    public string? Version { get; set; }

    /// <summary>File, line and value read; a dismissed suggestion returns only when this changes.</summary>
    public required JsonDocument Evidence { get; set; }

    /// <summary>Hash of product, version and the values read: the identity of a suggestion.</summary>
    public required string Fingerprint { get; set; }

    /// <summary><c>open</c>, <c>accepted</c> or <c>dismissed</c>.</summary>
    public string State { get; set; } = "open";

    public Guid? ComponentId { get; set; }

    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
