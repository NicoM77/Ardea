using Ardea.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Persistence;

public interface IUnitOfWorkFactory
{
    AsyncServiceScope CreateScope();
}

internal sealed class UnitOfWorkFactory(IServiceScopeFactory scopes, ITenantContext tenant) : IUnitOfWorkFactory
{
    public AsyncServiceScope CreateScope()
    {
        var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TenantContext>();
        if (tenant.OrganizationId is { } organization)
        {
            context.SetOrganization(organization);
        }

        if (tenant.UserId is { } user)
        {
            context.SetUser(user);
        }

        return scope;
    }
}
