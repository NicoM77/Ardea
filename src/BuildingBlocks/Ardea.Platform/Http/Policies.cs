namespace Ardea.Platform.Http;

/// <summary>
/// Authorization policy names every module may require. Iam registers them; modules only name them,
/// so they need no reference to Iam.
/// </summary>
public static class Policies
{
    /// <summary>The instance administrator (master plan 9.1).</summary>
    public const string InstanceAdmin = "instance-admin";
}

/// <summary>
/// Rate limit policies every module may require (master plan 9.3, 9.6). Iam registers them; modules only name them.
/// </summary>
public static class RateLimits
{
    /// <summary>
    /// Endpoints that call out of the instance while the request waits (a connection test, a test message, a search
    /// at GitHub) or build a large answer: per signed-in user, <c>ARDEA_RATE_LIMIT__OUTBOUND_PER_MINUTE</c> (10).
    /// </summary>
    public const string Outbound = "ardea-outbound";

    /// <summary>
    /// Requests without a session that the web app or a mail client makes in the normal course: the setup state, an
    /// invitation preview, the links of an external address (confirm, unsubscribe). Per client network, six times
    /// <c>ARDEA_RATE_LIMIT__AUTH_PER_MINUTE</c>.
    /// </summary>
    public const string Lookup = "ardea-auth-read";
}
