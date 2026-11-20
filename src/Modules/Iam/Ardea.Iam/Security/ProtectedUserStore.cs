using Ardea.Iam.Domain;
using Ardea.Iam.Persistence;
using Ardea.Platform.Secrets;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Ardea.Iam.Security;

internal sealed class ProtectedUserStore(IamDbContext context, ISecretProtector secrets, IdentityErrorDescriber? describer = null)
    : UserOnlyStore<ArdeaUser, IamDbContext, Guid>(context, describer)
{
    private const string Purpose = "iam:user-token";

    public override async Task<string?> GetTokenAsync(ArdeaUser user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        var stored = await base.GetTokenAsync(user, loginProvider, name, cancellationToken);
        return stored is null ? null : secrets.Unprotect(Purpose, stored);
    }

    public override Task SetTokenAsync(ArdeaUser user, string loginProvider, string name, string? value, CancellationToken cancellationToken) =>
        base.SetTokenAsync(user, loginProvider, name, value is null ? null : secrets.Protect(Purpose, value), cancellationToken);
}
