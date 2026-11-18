using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Ardea.Platform.Http;

/// <summary>Response metadata for the OpenAPI document, from which the web client is generated (master plan 10.1).</summary>
public static class EndpointMetadata
{
    public static RouteHandlerBuilder NoContent(this RouteHandlerBuilder builder) =>
        builder.Produces(StatusCodes.Status204NoContent);

    /// <summary>Problem details (RFC 9457) for the given status codes.</summary>
    public static RouteHandlerBuilder Problems(this RouteHandlerBuilder builder, params int[] statuses)
    {
        foreach (var status in statuses)
        {
            builder.ProducesProblem(status);
        }

        return builder;
    }
}
