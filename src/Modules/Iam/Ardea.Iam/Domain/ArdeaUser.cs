using Microsoft.AspNetCore.Identity;

namespace Ardea.Iam.Domain;

internal sealed class ArdeaUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }

    public bool IsInstanceAdmin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? TotpLastStep { get; set; }
}

internal sealed class UserSession
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(12);

    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(14);

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? UserAgent { get; set; }

    public string? IpAddress { get; set; }
}
