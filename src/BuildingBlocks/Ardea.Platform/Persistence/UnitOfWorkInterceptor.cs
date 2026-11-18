using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ardea.Platform.Persistence;

/// <summary>
/// Binds EF Core to the unit of work: when a context wants to open its connection, the unit of
/// work begins its transaction instead, and the context never closes the shared connection.
/// Contexts run with <c>AutoTransactionBehavior.Never</c>, so <c>SaveChanges</c> writes inside the
/// unit of work's transaction instead of starting its own.
/// </summary>
internal sealed class UnitOfWorkInterceptor(UnitOfWork unitOfWork) : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        unitOfWork.EnsureBegunAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return InterceptionResult.Suppress();
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        await unitOfWork.EnsureBegunAsync(cancellationToken);
        return InterceptionResult.Suppress();
    }

    public override InterceptionResult ConnectionClosing(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
        InterceptionResult.Suppress();

    public override ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
        ValueTask.FromResult(InterceptionResult.Suppress());
}
