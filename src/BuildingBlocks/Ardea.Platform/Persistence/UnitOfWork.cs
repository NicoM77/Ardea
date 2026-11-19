using System.Data;
using Ardea.Platform.Tenancy;
using Npgsql;

namespace Ardea.Platform.Persistence;

/// <summary>
/// One unit of work is one transaction across every module involved (master plan 9.2, ADR 023).
/// The platform provides connection and transaction; every module <c>DbContext</c> and every raw
/// command of the unit of work uses them. The transaction begins on first use and sets the tenant
/// context transaction-locally; nothing is kept per connection.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>The open connection inside the unit of work's transaction.</summary>
    ValueTask<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken);

    /// <summary>The connection object, opened or not; module contexts are bound to it.</summary>
    NpgsqlConnection Connection { get; }

    bool HasBegun { get; }

    /// <summary>Commits everything written in this unit of work. Without a commit, disposal rolls back.</summary>
    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="action"/> inside the transaction right before it commits, after everything the
    /// unit of work wrote. A module uses it to hand what it wrote on to its subscribers, which enqueue their
    /// follow-up jobs in the same transaction (master plan 3.3). An action may register further actions; a
    /// failing action fails the commit. Nothing runs when the unit of work is rolled back.
    /// </summary>
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

        // The tenant is fixed from here on: the EF filter and the database setting must agree.
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
            // Without the tenant setting the transaction must not be used at all.
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

    /// <summary>The job system commits a job's unit of work itself; handlers may not.</summary>
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
                // Its part of the work may be missing: what the rest wrote must not commit without it.
                _failed = true;
                _beforeCommit.Clear();
                throw;
            }
        }

        _completed = true;

        // The transaction is let go of here and never touched again: closing the connection hands the
        // physical connection back to the pool, and a later disposal of the transaction (or of this unit
        // of work) would then act on a connection that already belongs to someone else.
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
            // Nothing may run after the commit outside a transaction and without a tenant setting.
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
