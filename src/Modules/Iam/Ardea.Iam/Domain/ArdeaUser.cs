using Microsoft.AspNetCore.Identity;

namespace Ardea.Iam.Domain;

/// <summary>An account of the instance; global, one account with several memberships (master plan 9.1).</summary>
internal sealed class ArdeaUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }

    /// <summary>The first user of an instance manages sources, providers, SMTP and registration, not other organisations' data.</summary>
    public bool IsInstanceAdmin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The RFC 6238 time step of the last authenticator code accepted for this account. A code whose
    /// step is the same or earlier is refused, so each code works once (RFC 6238 §5.2, ASVS 2.8.4).
    /// Written only by the compare-and-set in <c>SingleUseAuthenticatorTokenProvider</c>.
    /// </summary>
    public long? TotpLastStep { get; set; }
}

/// <summary>
/// A server-side sign-in session (master plan 4.8, 9.3): the basis of "active sessions" and
/// "sign out everywhere". The cookie carries only its id; revoking the row ends the session.
/// </summary>
internal sealed class UserSession
{
    /// <summary>Sliding lifetime of a session without activity.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(12);

    /// <summary>Absolute lifetime, whatever the activity.</summary>
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
