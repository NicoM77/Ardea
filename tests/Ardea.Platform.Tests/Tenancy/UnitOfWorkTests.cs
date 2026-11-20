using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Ardea.Platform.Tests.Jobs;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

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
    public async Task Disposing_a_committed_unit_of_work_leaves_the_connection_to_its_next_user_alone()
    {
        await using var host = await JobTestHost.CreateAsync(server, _ => { });
        var connectionString = new NpgsqlConnectionStringBuilder(host.Database.Options.AppConnectionString("uow-reuse")) { MaxPoolSize = 1 };
        await using var dataSource = NpgsqlDataSource.Create(connectionString);

        var first = new UnitOfWork(dataSource, new TenantContext());
        await first.GetConnectionAsync(CancellationToken.None);
        await first.CommitAsync(CancellationToken.None);

        var second = new UnitOfWork(dataSource, new TenantContext());
        var connection = await second.GetConnectionAsync(CancellationToken.None);

        await first.DisposeAsync();

        await using var command = new NpgsqlCommand("SELECT 42", connection);
        Assert.Equal(42, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        await second.CommitAsync(CancellationToken.None);
        await second.DisposeAsync();
    }

    [Fact]
    public async Task Disposing_a_rolled_back_unit_of_work_again_leaves_the_transaction_of_the_connection_s_next_user_alone()
    {
        await using var host = await JobTestHost.CreateAsync(server, _ => { });
        var connectionString = new NpgsqlConnectionStringBuilder(host.Database.Options.AppConnectionString("uow-redispose")) { MaxPoolSize = 1 };
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var cancellationToken = TestContext.Current.CancellationToken;

        var first = new UnitOfWork(dataSource, new TenantContext());
        await first.GetConnectionAsync(cancellationToken);
        await first.DisposeAsync();

        var second = new UnitOfWork(dataSource, new TenantContext());
        var connection = await second.GetConnectionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand("SELECT generate_series(1, 2)", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            Assert.True(await reader.ReadAsync(cancellationToken));

            await first.DisposeAsync();

            Assert.True(await reader.ReadAsync(cancellationToken));
            Assert.Equal(2, reader.GetInt32(0));
        }

        await second.CommitAsync(cancellationToken);
        await second.DisposeAsync();
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
