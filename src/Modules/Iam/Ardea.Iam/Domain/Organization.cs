using Ardea.Platform.Persistence;

namespace Ardea.Iam.Domain;

/// <summary>
/// An organisation (master plan 9.1): the tenant. Its own row belongs to itself, so the tenant
/// filter applies to it like to every other organisation-owned row; the key column is the tenant id.
/// </summary>
internal sealed class Organization : IOrganizationOwned
{
    public Guid OrganizationId { get; set; }

    public required string Name { get; set; }

    /// <summary>The name in the web app's address (<c>/o/{slug}/…</c>); unique on the instance.</summary>
    public required string Slug { get; set; }

    /// <summary>Language of e-mails and analyses for the organisation, e.g. <c>de</c>.</summary>
    public string Locale { get; set; } = "en";

    /// <summary>IANA time zone for digests and deadlines.</summary>
    public string TimeZone { get; set; } = "Europe/Zurich";

    public OrganizationSettings Settings { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Settings of an organisation in <c>organization.settings</c> (master plan 4.8); never secrets.</summary>
internal sealed record OrganizationSettings
{
    /// <summary>Members must use two-factor authentication to work in the organisation (master plan 9.3).</summary>
    public bool RequireMfa { get; init; }

    /// <summary>
    /// Where requests to language models may go (master plan 7.8): <c>none</c>, <c>local_only</c> or
    /// <c>cloud_allowed</c>; empty means the default, <c>local_only</c>.
    /// </summary>
    public string? AiPolicy { get; init; }

    /// <summary>Where the setup wizard stands (master plan 11.6, 10.2); empty until it is first saved.</summary>
    public SetupState? Setup { get; init; }

    /// <summary>How much notification e-mails say (master plan 8.5): <c>full</c> (the default) or <c>minimal</c>.</summary>
    public string? EmailContent { get; init; }

    /// <summary>Members may mute a channel for level 5 too (master plan 8.3); off by default.</summary>
    public bool AllowMuteCritical { get; init; }
}

/// <summary>The setup wizard's progress: the steps saved so far, the project it created and when it finished.</summary>
internal sealed record SetupState
{
    public IReadOnlyList<string> StepsDone { get; init; } = [];

    public Guid? FirstProjectId { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>A user's membership with one role in one organisation.</summary>
internal sealed class Membership : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid UserId { get; set; }

    /// <summary><c>owner</c>, <c>admin</c>, <c>member</c> or <c>viewer</c>; exactly one owner per organisation.</summary>
    public required string Role { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An open invitation; the link carries the token, the database only its hash (master plan 4.8).</summary>
internal sealed class Invitation : IOrganizationOwned
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Email { get; set; }

    public required string NormalizedEmail { get; set; }

    /// <summary><c>admin</c>, <c>member</c> or <c>viewer</c>; ownership is transferred, not invited.</summary>
    public required string Role { get; set; }

    /// <summary>SHA-256 of the token, hex.</summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public Guid InvitedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
