using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Http;

internal sealed class SourceHttpFactory(IHttpMessageHandlerFactory handlers, HostRateLimits limits)
{
    public HttpClient Create(SourceDescriptor descriptor, SyncMode mode)
    {
        var backfill = mode == SyncMode.Backfill;
        var inner = handlers.CreateHandler(backfill ? SourceHttp.BackfillClientName : SourceHttp.ClientName);
        var guard = new SourceGuardHandler(descriptor, backfill ? descriptor.MaxBackfillBytes : descriptor.MaxResponseBytes, limits) { InnerHandler = inner };
        return new HttpClient(guard, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
