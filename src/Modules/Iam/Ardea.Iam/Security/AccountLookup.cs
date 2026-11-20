using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Npgsql;

namespace Ardea.Iam.Security;

internal static class AccountLookup
{
    public static async Task<Guid?> UserIdByEmailAsync(NpgsqlDataSource dataSource, string normalizedEmail, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT iam.user_id_by_email($1)");
        command.Parameters.Add(new NpgsqlParameter { Value = normalizedEmail });
        return await command.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    public static bool TryActAs(TenantContext tenant, Guid userId)
    {
        if (tenant.UserId is { } current && current != userId)
        {
            return false;
        }

        tenant.SetUser(userId);
        return true;
    }

    public static async Task LockAsync(IUnitOfWork unitOfWork, Guid userId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT 1 FROM iam.\"user\" WHERE id = $1 FOR UPDATE", await unitOfWork.GetConnectionAsync(cancellationToken));
        command.Parameters.Add(new NpgsqlParameter { Value = userId });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
