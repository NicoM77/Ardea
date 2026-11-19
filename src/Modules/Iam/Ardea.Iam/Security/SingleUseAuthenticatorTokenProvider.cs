using Ardea.Iam.Domain;
using Ardea.Iam.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ardea.Iam.Security;

/// <summary>
/// Identity's authenticator provider (<see cref="TokenOptions.DefaultAuthenticatorProvider"/>) with one
/// addition: a code works once per account (RFC 6238 §5.2, ASVS 2.8.4; issue #85). Every place that checks
/// an authenticator code goes through it: the second sign-in step and turning two-factor on and off.
/// </summary>
/// <remarks>
/// The step a code matched is claimed with a compare-and-set on the account row, in the request's unit of
/// work: <c>UPDATE … SET totp_last_step = step WHERE id = user AND (totp_last_step IS NULL OR totp_last_step &lt; step)</c>.
/// Of two requests with the same code, the second waits for the first one's row lock and then finds the
/// step taken, so exactly one succeeds. A request that fails later rolls the claim back with everything else.
/// </remarks>
internal sealed partial class SingleUseAuthenticatorTokenProvider(
    IamDbContext db, TimeProvider time, IHttpContextAccessor http, ILogger<SingleUseAuthenticatorTokenProvider> logger)
    : IUserTwoFactorTokenProvider<ArdeaUser>
{
    /// <summary>The app makes the code; nothing is sent.</summary>
    public Task<string> GenerateAsync(string purpose, UserManager<ArdeaUser> manager, ArdeaUser user) => Task.FromResult(string.Empty);

    public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<ArdeaUser> manager, ArdeaUser user) =>
        !string.IsNullOrWhiteSpace(await manager.GetAuthenticatorKeyAsync(user));

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<ArdeaUser> manager, ArdeaUser user)
    {
        var key = await manager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key) || AuthenticatorCode.MatchingStep(key, token, time.GetUtcNow()) is not { } step)
        {
            return false;
        }

        var claimed = await db.Users
            .Where(u => u.Id == user.Id && (u.TotpLastStep == null || u.TotpLastStep < step))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TotpLastStep, step), http.HttpContext?.RequestAborted ?? CancellationToken.None);
        if (claimed != 1)
        {
            // Not a secret: which account, not the code. The caller answers exactly as for a wrong code.
            LogReused(user.Id);
            return false;
        }

        // The tracked account now agrees with the row, without a second write of the column.
        user.TotpLastStep = step;
        var entry = db.Entry(user);
        if (entry.State != EntityState.Detached)
        {
            entry.Property(u => u.TotpLastStep).OriginalValue = step;
        }

        return true;
    }

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "An authenticator code for account {UserId} was refused: its time step had already been used")]
    private partial void LogReused(Guid userId);
}
