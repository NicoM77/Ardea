using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ardea.Platform.Persistence;

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
