using Ardea.Platform.Persistence;

namespace Ardea.Iam.Domain;

internal sealed class Organization : IOrganizationOwned
{
    public Guid OrganizationId { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public string Locale { get; set; } = "en";

    public string TimeZone { get; set; } = "Europe/Zurich";

    public OrganizationSettings Settings { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed record OrganizationSettings
{
    public bool RequireMfa { get; init; }

    public string? AiPolicy { get; init; }

    public SetupState? Setup { get; init; }

    public string? EmailContent { get; init; }

    public bool AllowMuteCritical { get; init; }
}

internal sealed record SetupState
{
    public IReadOnlyList<string> StepsDone { get; init; } = [];

    public Guid? FirstProjectId { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}

internal sealed class Membership : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid UserId { get; set; }

    public required string Role { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class Invitation : IOrganizationOwned
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Email { get; set; }

    public required string NormalizedEmail { get; set; }

    public required string Role { get; set; }

    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public Guid InvitedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
