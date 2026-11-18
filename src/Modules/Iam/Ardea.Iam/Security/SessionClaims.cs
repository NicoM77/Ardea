namespace Ardea.Iam.Security;

internal static class SessionClaims
{
    /// <summary>Id of the server-side session in <c>iam.session</c>.</summary>
    public const string SessionId = "ardea:sid";

    /// <summary>
    /// Cookie names. With an HTTPS public address (every real instance) they carry the <c>__Host-</c>
    /// prefix and the Secure flag; only a development instance on plain <c>http://localhost</c>
    /// goes without, because browsers reject <c>__Host-</c> cookies that are not Secure.
    /// </summary>
    public static string CookieName(bool secure) => secure ? "__Host-ardea" : "ardea";

    public static string CsrfCookieName(bool secure) => secure ? "__Host-ardea-csrf" : "ardea-csrf";

    public static string TwoFactorCookieName(bool secure) => secure ? "__Host-ardea-2fa" : "ardea-2fa";

    /// <summary>Readable by the web app, which echoes it in <c>X-XSRF-TOKEN</c> (Angular's default names).</summary>
    public const string XsrfCookieName = "XSRF-TOKEN";

    public const string XsrfHeaderName = "X-XSRF-TOKEN";
}
