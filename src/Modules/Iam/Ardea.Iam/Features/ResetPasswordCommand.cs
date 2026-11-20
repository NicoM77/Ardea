using System.Security.Cryptography;
using Ardea.Iam.Domain;
using Ardea.Iam.Persistence;
using Ardea.Platform.Audit;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Iam.Features;

internal static class ResetPasswordCommand
{
    public static readonly MaintenanceCommand Definition = new(
        "reset-password",
        "reset-password <email> [--disable-mfa]",
        "Set a new generated password for an account, lift a lockout and end its sessions",
        RunAsync);

    private static async Task<int> RunAsync(IServiceProvider services, string[] args, CancellationToken cancellationToken)
    {
        var email = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (email is null)
        {
            await Console.Error.WriteLineAsync("Usage: ardea admin " + Definition.Usage);
            return 64;
        }

        var users = services.GetRequiredService<UserManager<ArdeaUser>>();
        var userId = await Security.AccountLookup.UserIdByEmailAsync(services.GetRequiredService<Npgsql.NpgsqlDataSource>(), users.NormalizeEmail(email), cancellationToken);
        if (userId is { } id)
        {
            services.GetRequiredService<Platform.Tenancy.TenantContext>().SetUser(id);
        }

        var user = userId is null ? null : await users.FindByIdAsync(userId.Value.ToString());
        if (user is null)
        {
            await Console.Error.WriteLineAsync($"No account with the e-mail address {email}.");
            return 1;
        }

        var password = Generate();
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(user, token, password);
        if (!reset.Succeeded)
        {
            await Console.Error.WriteLineAsync(string.Join(" ", reset.Errors.Select(e => e.Description)));
            return 1;
        }

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        var disableMfa = args.Contains("--disable-mfa");
        if (disableMfa && user.TwoFactorEnabled)
        {
            await users.SetTwoFactorEnabledAsync(user, false);
            await users.ResetAuthenticatorKeyAsync(user);
        }

        var db = services.GetRequiredService<IamDbContext>();
        await AuthEndpoints.RevokeSessionsAsync(db, user.Id, except: null, services.GetRequiredService<TimeProvider>(), cancellationToken);
        services.GetRequiredService<AuditOrigin>().Origin = "cli";
        await services.GetRequiredService<IAuditLog>().RecordAsync(
            new AuditEntry("admin.password_reset", "user", user.Id.ToString(), new { mfaDisabled = disableMfa }) { InstanceEvent = true },
            cancellationToken);
        await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Console.WriteLine($"New password for {user.Email}: {password}");
        Console.WriteLine("Every session of the account has ended. Ask the person to change the password after signing in.");
        if (disableMfa)
        {
            Console.WriteLine("Two-factor authentication is off; the person can turn it on again in the account settings.");
        }

        return 0;
    }

    internal static string Generate()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        var groups = Enumerable.Range(0, 5).Select(_ =>
            new string([.. Enumerable.Range(0, 5).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)])]));
        return string.Join('-', groups);
    }
}
