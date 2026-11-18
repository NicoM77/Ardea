using Ardea.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Persistence;

/// <summary>
/// Opens additional, short units of work. Jobs that talk to the outside world read in one, call out
/// without an open transaction, and write their result in the job's own unit of work; fetches with
/// checkpoints commit each stretch separately (master plan 9.2).
/// </summary>
public interface IUnitOfWorkFactory
{
    /// <summary>A new scope with its own unit of work and the same tenant as the caller.</summary>
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
