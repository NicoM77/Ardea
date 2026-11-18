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
}

internal sealed class UnitOfWork(NpgsqlDataSource dataSource, ITenantContext tenant) : IUnitOfWork, IAsyncDisposable
{
    private NpgsqlTransaction? _transaction;
    private bool _completed;
    private bool _commitReserved;

    public NpgsqlConnection Connection { get; } = dataSource.CreateConnection();

    public bool HasBegun => _transaction is not null;

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
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_commitReserved)
        {
            throw new InvalidOperationException("A job's unit of work is committed by the job system together with the job's completion.");
        }

        return CommitCoreAsync(cancellationToken);
    }

    /// <summary>The job system commits a job's unit of work itself; handlers may not.</summary>
    internal void ReserveCommitForOwner() => _commitReserved = true;

    internal Task CommitAsOwnerAsync(CancellationToken cancellationToken) => CommitCoreAsync(cancellationToken);

    private async Task CommitCoreAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        _completed = true;
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
        }

        // Nothing may run after the commit outside a transaction and without a tenant setting.
        await Connection.CloseAsync();
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
