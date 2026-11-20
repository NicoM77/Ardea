using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Ardea.Platform.Http;
using Ardea.Platform.Live;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ardea.Assessment.Features;

internal static class LiveEndpoints
{
    internal static TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(25);

    public static void Map(IEndpointRouteBuilder api) =>
        api.MapOrganization().WithTags("Live")
            .MapGet("/events", StreamAsync).WithName("streamOrganizationEvents")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream").Problems(403, 404);

    private static async Task<IResult> StreamAsync(IOrganizationSignals signals, IUnitOfWork unitOfWork, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var organization = tenant.RequiredOrganizationId;
        await unitOfWork.CommitAsync(cancellationToken);
        return TypedResults.ServerSentEvents(EventsAsync(signals.Subscribe(organization), cancellationToken));
    }

    private static async IAsyncEnumerable<SseItem<string>> EventsAsync(OrganizationSubscription subscription, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var _ = subscription;
        yield return new SseItem<string>("{}", "ready");
        while (!cancellationToken.IsCancellationRequested)
        {
            string? kind = null;
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                wait.CancelAfter(PingInterval);
                try
                {
                    if (!await subscription.Reader.WaitToReadAsync(wait.Token))
                    {
                        yield break;
                    }

                    while (subscription.Reader.TryRead(out var next))
                    {
                        kind = kind == "resync" ? kind : next;
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    kind = "ping";
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
            }

            if (kind is not null)
            {
                yield return new SseItem<string>("{}", kind);
            }
        }
    }
}
