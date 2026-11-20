using System.Data;
using Ardea.Platform.Tenancy;
using Npgsql;

namespace Ardea.Platform.Persistence;

public interface IUnitOfWork
{
    ValueTask<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken);

    NpgsqlConnection Connection { get; }

    bool HasBegun { get; }

    Task CommitAsync(CancellationToken cancellationToken);

    void BeforeCommit(Func<CancellationToken, Task> action);
}

internal sealed class UnitOfWork(NpgsqlDataSource dataSource, ITenantContext tenant) : IUnitOfWork, IAsyncDisposable
{
    private NpgsqlTransaction? _transaction;
    private bool _completed;
    private bool _commitReserved;
    private bool _failed;
    private readonly Queue<Func<CancellationToken, Task>> _beforeCommit = new();

    public NpgsqlConnection Connection { get; } = dataSource.CreateConnection();

    public bool HasBegun { get; private set; }

    public async ValueTask<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        await EnsureBegunAsync(cancellationToken);
        return Connection;
    }

    internal async ValueTask EnsureBegunAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        if (_transaction is not null)
        {
            return;
        }

        if (Connection.State != ConnectionState.Open)
        {
            await Connection.OpenAsync(cancellationToken);
        }

        (tenant as TenantContext)?.Freeze();
        var transaction = await Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT set_config('ardea.org_id', $1, true), set_config('ardea.user_id', $2, true)", Connection, transaction);
            command.Parameters.Add(new NpgsqlParameter { Value = tenant.OrganizationId?.ToString() ?? string.Empty });
            command.Parameters.Add(new NpgsqlParameter { Value = tenant.UserId?.ToString() ?? string.Empty });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            await transaction.DisposeAsync();
            await Connection.CloseAsync();
            throw;
        }

        _transaction = transaction;
        HasBegun = true;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_commitReserved)
        {
            throw new InvalidOperationException("A job's unit of work is committed by the job system together with the job's completion.");
        }

        return CommitCoreAsync(cancellationToken);
    }

    public void BeforeCommit(Func<CancellationToken, Task> action)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        _beforeCommit.Enqueue(action);
    }

    internal void ReserveCommitForOwner() => _commitReserved = true;

    internal Task CommitAsOwnerAsync(CancellationToken cancellationToken) => CommitCoreAsync(cancellationToken);

    private async Task CommitCoreAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        if (_failed)
        {
            throw new InvalidOperationException("An action before the commit failed; the unit of work can only be rolled back.");
        }

        while (_beforeCommit.TryDequeue(out var action))
        {
            try
            {
                await action(cancellationToken);
            }
            catch
            {
                _failed = true;
                _beforeCommit.Clear();
                throw;
            }
        }

        _completed = true;

        var transaction = _transaction;
        _transaction = null;
        try
        {
            if (transaction is not null)
            {
                try
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                finally
                {
                    await transaction.DisposeAsync();
                }
            }
        }
        finally
        {
            await Connection.CloseAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _completed = true;
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
        }

        await Connection.DisposeAsync();
    }
}
