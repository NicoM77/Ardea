using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Ardea.Platform.Http;

public sealed record TokenAuthenticated;

public static class EndpointMetadata
{
    public static RouteHandlerBuilder NoContent(this RouteHandlerBuilder builder) =>
        builder.Produces(StatusCodes.Status204NoContent);

    public static TBuilder AuthenticatedByToken<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new TokenAuthenticated()).AllowAnonymous();

    public static RouteHandlerBuilder Problems(this RouteHandlerBuilder builder, params int[] statuses)
    {
        foreach (var status in statuses)
        {
            builder.ProducesProblem(status);
        }

        return builder;
    }
}
