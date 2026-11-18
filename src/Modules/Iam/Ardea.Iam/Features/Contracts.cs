namespace Ardea.Iam.Features;

// Request and response bodies of the Iam endpoints. Nullable inputs: the handlers validate them and
// answer with problem details instead of a binding error.

internal sealed record SetupStatusResponse(bool SetupRequired);

internal sealed record SetupRequest(string? SetupToken, string? Email, string? DisplayName, string? Password);

internal sealed record LoginRequest(string? Email, string? Password);

internal sealed record LoginResponse(string Status)
{
    public static readonly LoginResponse SignedIn = new("signedIn");
    public static readonly LoginResponse TwoFactorRequired = new("twoFactorRequired");
}

internal sealed record TwoFactorRequest(string? Code, bool IsRecoveryCode);

internal sealed record RegisterRequest(string? InvitationToken, string? Email, string? DisplayName, string? Password);

internal sealed record ForgotPasswordRequest(string? Email);

internal sealed record ConfirmEmailRequest(Guid? UserId, string? Token);

internal sealed record ResetPasswordRequest(string? Email, string? Token, string? NewPassword);

internal sealed record SessionUser(Guid Id, string Email, string DisplayName, bool IsInstanceAdmin, bool TwoFactorEnabled);

internal sealed record SessionOrganization(Guid Id, string Slug, string Name, string Role);

internal sealed record SessionResponse(SessionUser User, IReadOnlyList<SessionOrganization> Organizations);

internal sealed record UpdateAccountRequest(string? DisplayName);

internal sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

internal sealed record MfaSetupRequest(string? Password);

internal sealed record MfaSetupResponse(string SharedKey, string AuthenticatorUri);

internal sealed record EnableMfaRequest(string? Code);

internal sealed record DisableMfaRequest(string? Password, string? Code);

internal sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

internal sealed record SessionInfo(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, string? UserAgent, string? IpAddress, bool Current);
