using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Npgsql;

namespace Ardea.Iam.Security;

/// <summary>
/// The system operation "sign-in" of master plan 9.2: which account belongs to an e-mail address,
/// asked before any user context exists. Row-level security hides every account but the current
/// user's, so the flows that start without one (sign-in, registration, setup, password reset) find
/// the account here, on a connection of their own, and make it the user of their unit of work
/// before it begins (ADR 029).
/// </summary>
internal static class AccountLookup
{
    public static async Task<Guid?> UserIdByEmailAsync(NpgsqlDataSource dataSource, string normalizedEmail, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT iam.user_id_by_email($1)");
        command.Parameters.Add(new NpgsqlParameter { Value = normalizedEmail });
        return await command.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    /// <summary>
    /// Makes <paramref name="userId"/> the user of the request's unit of work. False when another
    /// account is already signed in in this browser: the caller answers instead of mixing two accounts.
    /// </summary>
    public static bool TryActAs(TenantContext tenant, Guid userId)
    {
        if (tenant.UserId is { } current && current != userId)
        {
            return false;
        }

        tenant.SetUser(userId);
        return true;
    }

    /// <summary>
    /// Locks the account row for the rest of the unit of work, before the account is loaded. Every flow that
    /// checks an authenticator or recovery code takes it first, so two of them for one account run one after
    /// the other and each loads what the previous one committed: a code claimed or redeemed by the first is
    /// seen as used by the second, and no stale account or recovery-code list is written back (ADR 045).
    /// </summary>
    public static async Task LockAsync(IUnitOfWork unitOfWork, Guid userId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT 1 FROM iam.\"user\" WHERE id = $1 FOR UPDATE", await unitOfWork.GetConnectionAsync(cancellationToken));
        command.Parameters.Add(new NpgsqlParameter { Value = userId });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
