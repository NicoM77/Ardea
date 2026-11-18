using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Ardea.Platform.Tests.Jobs;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Tests.Tenancy;

public sealed class UnitOfWorkTests(PostgresServer server)
{
    [Fact]
    public async Task Nothing_runs_after_commit_outside_the_transaction()
    {
        await using var host = await JobTestHost.CreateAsync(server, _ => { });
        await using var scope = host.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.GetConnectionAsync(CancellationToken.None);
        await unitOfWork.CommitAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await unitOfWork.GetConnectionAsync(CancellationToken.None));
        Assert.Equal(System.Data.ConnectionState.Closed, unitOfWork.Connection.State);
    }

    [Fact]
    public async Task Tenant_cannot_change_once_the_transaction_has_begun()
    {
        await using var host = await JobTestHost.CreateAsync(server, _ => { });
        await using var scope = host.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().GetConnectionAsync(CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => tenant.SetOrganization(Guid.CreateVersion7()));
    }

    [Fact]
    public void Tenant_cannot_switch_to_another_organisation()
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(Guid.CreateVersion7());

        Assert.Throws<InvalidOperationException>(() => tenant.SetOrganization(Guid.CreateVersion7()));
    }
}
