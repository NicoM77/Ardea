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
