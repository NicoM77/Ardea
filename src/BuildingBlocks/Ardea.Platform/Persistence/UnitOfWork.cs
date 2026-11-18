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

        _transaction = await Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT set_config('ardea.org_id', $1, true), set_config('ardea.user_id', $2, true)", Connection, _transaction);
        command.Parameters.Add(new NpgsqlParameter { Value = tenant.OrganizationId?.ToString() ?? string.Empty });
        command.Parameters.Add(new NpgsqlParameter { Value = tenant.UserId?.ToString() ?? string.Empty });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        _completed = true;
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(cancellationToken);
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
