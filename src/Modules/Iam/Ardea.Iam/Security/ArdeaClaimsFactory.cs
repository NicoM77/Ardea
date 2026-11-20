using System.Security.Claims;
using Ardea.Iam.Contracts;
using Ardea.Iam.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Ardea.Iam.Security;

internal sealed class ArdeaClaimsFactory(UserManager<ArdeaUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ArdeaUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ArdeaUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.IsInstanceAdmin)
        {
            identity.AddClaim(new Claim(IamPolicies.InstanceAdminClaim, "true"));
        }

        return identity;
    }
}
