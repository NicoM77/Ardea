using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Http;

/// <summary>Builds the guarded client for one run of one source.</summary>
internal sealed class SourceHttpFactory(IHttpMessageHandlerFactory handlers, HostRateLimits limits)
{
    public HttpClient Create(SourceDescriptor descriptor, SyncMode mode)
    {
        var backfill = mode == SyncMode.Backfill;
        var inner = handlers.CreateHandler(backfill ? SourceHttp.BackfillClientName : SourceHttp.ClientName);
        var guard = new SourceGuardHandler(descriptor, backfill ? descriptor.MaxBackfillBytes : descriptor.MaxResponseBytes, limits) { InnerHandler = inner };
        // The overall time is bounded by the resilience pipeline and the job's time limit.
        return new HttpClient(guard, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
