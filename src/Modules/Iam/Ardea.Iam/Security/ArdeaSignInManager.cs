using System.Security.Claims;
using Ardea.Iam.Domain;
using Ardea.Iam.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ardea.Iam.Security;

internal sealed class ArdeaSignInManager(
    UserManager<ArdeaUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ArdeaUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ArdeaUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ArdeaUser> confirmation,
    IamDbContext db,
    TimeProvider time)
    : SignInManager<ArdeaUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task SignInWithClaimsAsync(ArdeaUser user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        var now = time.GetUtcNow();
        var http = Context;
        var session = new UserSession
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + UserSession.AbsoluteLifetime,
            UserAgent = Truncate(http.Request.Headers.UserAgent.ToString(), 500),
            IpAddress = http.Connection.RemoteIpAddress?.ToString(),
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(http.RequestAborted);

        authenticationProperties ??= new AuthenticationProperties();
        authenticationProperties.IsPersistent = true;
        authenticationProperties.AllowRefresh = true;
        await base.SignInWithClaimsAsync(user, authenticationProperties, [.. additionalClaims, new Claim(SessionClaims.SessionId, session.Id.ToString())]);
    }

    private static string? Truncate(string value, int length) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= length ? value : value[..length];
}
