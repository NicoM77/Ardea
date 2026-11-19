using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Ardea.Platform.Http;

/// <summary>
/// Marks an endpoint that authenticates its caller by a token in the request, never by the session cookie,
/// e.g. the unsubscribe link of an e-mail (RFC 8058 one-click). A cross-site request to it gains nothing, so
/// the CSRF check (master plan 9.3) lets it through; mail clients post to it without a browser.
/// </summary>
public sealed record TokenAuthenticated;

/// <summary>Response metadata for the OpenAPI document, from which the web client is generated (master plan 10.1).</summary>
public static class EndpointMetadata
{
    public static RouteHandlerBuilder NoContent(this RouteHandlerBuilder builder) =>
        builder.Produces(StatusCodes.Status204NoContent);

    /// <summary>The endpoint takes no session: its token is the authentication, so no CSRF token is required.</summary>
    public static TBuilder AuthenticatedByToken<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new TokenAuthenticated()).AllowAnonymous();

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
