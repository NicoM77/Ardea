using Ardea.Platform.Persistence;
using Ardea.Platform.Tests.Jobs;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ardea.Platform.Tests.Persistence;

public sealed class UnitOfWorkTests(PostgresServer server)
{
    [Fact]
    public async Task An_action_before_the_commit_writes_in_the_same_transaction()
    {
        await using var host = await JobTestHost.CreateAsync(server, _ => { });
        await using var scope = host.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await InsertAsync(unitOfWork, "change");
        unitOfWork.BeforeCommit(ct => InsertAsync(unitOfWork, "follow-up", ct));

        await unitOfWork.CommitAsync(CancellationToken.None);

        Assert.Equal(2, await host.CountAsync("SELECT count(*) FROM public.test_business"));
    }

    [Fact]
    public async Task A_failing_action_before_the_commit_leaves_the_unit_of_work_uncommittable()
    {
        await using var host = await JobTestHost.CreateAsync(server, _ => { });
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await InsertAsync(unitOfWork, "change");
            unitOfWork.BeforeCommit(_ => throw new InvalidOperationException("subscriber failed"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.CommitAsync(CancellationToken.None));
            var again = await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.CommitAsync(CancellationToken.None));
            Assert.Contains("rolled back", again.Message, StringComparison.Ordinal);
        }

        Assert.Equal(0, await host.CountAsync("SELECT count(*) FROM public.test_business"));
    }

    private static async Task InsertAsync(IUnitOfWork unitOfWork, string name, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("INSERT INTO public.test_business (name) VALUES ($1)", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = name });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
