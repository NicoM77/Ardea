using Ardea.Iam.Domain;
using Ardea.Platform.Email;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Iam.Features;

internal sealed record SendPasswordResetEmail(Guid UserId) : IJob
{
    public static string Kind => "iam.send-password-reset";
}

internal sealed record SendEmailConfirmation(Guid UserId) : IJob
{
    public static string Kind => "iam.send-email-confirmation";
}

internal sealed record SendAlreadyRegisteredNotice(Guid UserId) : IJob
{
    public static string Kind => "iam.send-already-registered";
}

internal sealed class AccountEmailHandlers(IUnitOfWorkFactory units, IEmailSender email, IConfiguration configuration)
    : IJobHandler<SendPasswordResetEmail>, IJobHandler<SendEmailConfirmation>, IJobHandler<SendAlreadyRegisteredNotice>
{
    public async Task HandleAsync(SendAlreadyRegisteredNotice job, JobContext context, CancellationToken cancellationToken)
    {
        var (user, _) = await ReadAsync(job.UserId, (_, _) => Task.FromResult(string.Empty));
        if (user is null)
        {
            return;
        }

        await email.SendAsync(EmailTemplate.Render(
            user.Email!,
            "Your Ardea account already exists",
            $"Hello {user.DisplayName},",
            ["Someone tried to register a new Ardea account with this address. You already have one, so nothing was created.",
             "If it was you, sign in instead; if you forgot your password, reset it from the sign-in page. If it was not you, ignore this e-mail."],
            "Sign in",
            new Uri(PublicUrl(), "sign-in"),
            "You receive this e-mail because of a registration attempt on " + PublicUrl().Host + "."),
            cancellationToken);
    }

    public async Task HandleAsync(SendPasswordResetEmail job, JobContext context, CancellationToken cancellationToken)
    {
        var (user, token) = await ReadAsync(job.UserId, (users, u) => users.GeneratePasswordResetTokenAsync(u));
        if (user is null)
        {
            return;
        }

        var link = Link("reset-password", new() { ["email"] = user.Email!, ["token"] = token! });
        await email.SendAsync(EmailTemplate.Render(
            user.Email!,
            "Reset your Ardea password",
            $"Hello {user.DisplayName},",
            ["Someone asked to reset the password of your Ardea account. If that was you, choose a new password with the link below. It works for two hours.",
             "If it was not you, ignore this e-mail; your password stays as it is."],
            "Choose a new password",
            link,
            "You receive this e-mail because of a request on the Ardea instance at " + PublicUrl().Host + "."),
            cancellationToken);
    }

    public async Task HandleAsync(SendEmailConfirmation job, JobContext context, CancellationToken cancellationToken)
    {
        var (user, token) = await ReadAsync(job.UserId, (users, u) => users.GenerateEmailConfirmationTokenAsync(u));
        if (user is null || user.EmailConfirmed)
        {
            return;
        }

        var link = Link("confirm-email", new() { ["userId"] = user.Id.ToString(), ["token"] = token! });
        await email.SendAsync(EmailTemplate.Render(
            user.Email!,
            "Confirm your e-mail address for Ardea",
            $"Hello {user.DisplayName},",
            ["Please confirm that this address belongs to you. Ardea sends notifications only to confirmed addresses."],
            "Confirm e-mail address",
            link,
            "You receive this e-mail because an account was created with this address on " + PublicUrl().Host + "."),
            cancellationToken);
    }

    private async Task<(ArdeaUser? User, string? Token)> ReadAsync(Guid userId, Func<UserManager<ArdeaUser>, ArdeaUser, Task<string>> token)
    {
        await using var scope = units.CreateScope();
        scope.ServiceProvider.GetRequiredService<Platform.Tenancy.TenantContext>().SetUser(userId);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ArdeaUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null ? (null, null) : (user, await token(users, user));
    }

    private Uri Link(string path, Dictionary<string, string?> query) =>
        new(QueryHelpers.AddQueryString(new Uri(PublicUrl(), path).AbsoluteUri, query));

    private Uri PublicUrl()
    {
        var url = configuration["PUBLIC_URL"] ?? throw new InvalidOperationException("ARDEA_PUBLIC_URL is not set.");
        return new Uri(url.EndsWith('/') ? url : url + "/");
    }
}
