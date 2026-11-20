using Ardea.Iam.Persistence;
using Ardea.Platform.Email;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Iam.Features;

internal sealed record SendInvitationEmail(Guid InvitationId) : IJob
{
    public static string Kind => "iam.send-invitation";
}

internal sealed class InvitationEmailHandler(IUnitOfWorkFactory units, IEmailSender email, IConfiguration configuration) : IJobHandler<SendInvitationEmail>
{
    public async Task HandleAsync(SendInvitationEmail job, JobContext context, CancellationToken cancellationToken)
    {
        var token = OrganizationDirectory.NewToken();
        string address, organization, inviter, role;
        DateTimeOffset expires;
        await using (var scope = units.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IamDbContext>();
            var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Id == job.InvitationId, cancellationToken);
            if (invitation is null)
            {
                return;
            }

            invitation.TokenHash = OrganizationDirectory.HashToken(token);
            await db.SaveChangesAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
            (address, role, expires) = (invitation.Email, invitation.Role, invitation.ExpiresAt);
            organization = await db.Organizations.Select(o => o.Name).SingleAsync(cancellationToken);
            inviter = await db.Users.Where(u => u.Id == invitation.InvitedBy).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken)
                ?? OrganizationEndpoints.FormerMember;
        }

        var link = OrganizationEndpoints.InvitationLink(configuration, token);
        await email.SendAsync(EmailTemplate.Render(
            address,
            $"{inviter} invites you to {organization} on Ardea",
            "Hello,",
            [$"{inviter} invites you to join the organisation {organization} on Ardea as {RoleName(role)}. Ardea keeps track of security vulnerabilities in the technologies the organisation uses.",
             $"The invitation is for this address and works until {expires:yyyy-MM-dd}. If you have no account yet, you create one on the way."],
            "Accept the invitation",
            link,
            "You receive this e-mail because a member of " + organization + " invited this address on " + link.Host + ". If you did not expect it, ignore it."),
            cancellationToken);
    }

    private static string RoleName(string role) => role switch
    {
        "admin" => "an administrator",
        "member" => "a member",
        _ => "a viewer",
    };
}
