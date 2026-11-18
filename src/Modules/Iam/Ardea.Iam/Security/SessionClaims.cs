namespace Ardea.Iam.Security;

internal static class SessionClaims
{
    /// <summary>Id of the server-side session in <c>iam.session</c>.</summary>
    public const string SessionId = "ardea:sid";

    public const string CookieName = "__Host-ardea";

    public const string CsrfCookieName = "__Host-ardea-csrf";

    /// <summary>Readable by the web app, which echoes it in <c>X-XSRF-TOKEN</c> (Angular's default names).</summary>
    public const string XsrfCookieName = "XSRF-TOKEN";

    public const string XsrfHeaderName = "X-XSRF-TOKEN";
}
