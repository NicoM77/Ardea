using Ardea.Iam.Contracts;
using Ardea.Iam.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ardea.Iam.Features;

internal sealed class InstanceAdministrators(NpgsqlDataSource dataSource, IServiceScopeFactory scopes) : IInstanceAdministrators
{
    public async Task<IReadOnlyList<InstanceAdministrator>> ListReachableAsync(CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        await using (var command = dataSource.CreateCommand("SELECT id FROM iam.instance_administrators() AS id"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetGuid(0));
            }
        }

        var administrators = new List<InstanceAdministrator>();
        foreach (var id in ids)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetUser(id);
            var db = scope.ServiceProvider.GetRequiredService<IamDbContext>();
            var administrator = await db.Users.AsNoTracking()
                .Where(u => u.Id == id && u.IsInstanceAdmin && u.EmailConfirmed && u.Email != null)
                .Select(u => new InstanceAdministrator(u.Id, u.DisplayName, u.Email!))
                .SingleOrDefaultAsync(cancellationToken);
            if (administrator is not null)
            {
                administrators.Add(administrator);
            }
        }

        return administrators;
    }
}
