using Ardea.Iam.Domain;
using Ardea.Iam.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ardea.Iam.Security;

internal sealed partial class SingleUseAuthenticatorTokenProvider(
    IamDbContext db, TimeProvider time, IHttpContextAccessor http, ILogger<SingleUseAuthenticatorTokenProvider> logger)
    : IUserTwoFactorTokenProvider<ArdeaUser>
{
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
            LogReused(user.Id);
            return false;
        }

        return true;
    }

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "An authenticator code for account {UserId} was refused: its time step had already been used")]
    private partial void LogReused(Guid userId);
}
