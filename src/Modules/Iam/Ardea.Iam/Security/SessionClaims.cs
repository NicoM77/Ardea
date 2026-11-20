namespace Ardea.Iam.Security;

internal static class SessionClaims
{
    public const string SessionId = "ardea:sid";

    public static string CookieName(bool secure) => secure ? "__Host-ardea" : "ardea";

    public static string CsrfCookieName(bool secure) => secure ? "__Host-ardea-csrf" : "ardea-csrf";

    public static string TwoFactorCookieName(bool secure) => secure ? "__Host-ardea-2fa" : "ardea-2fa";

    public const string XsrfCookieName = "XSRF-TOKEN";

    public const string XsrfHeaderName = "X-XSRF-TOKEN";
}
