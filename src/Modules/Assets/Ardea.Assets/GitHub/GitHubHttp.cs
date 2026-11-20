using System.Net.Http.Headers;
using System.Reflection;

namespace Ardea.Assets.GitHub;

internal static class GitHubHttp
{
    public const string ApiVersion = "2022-11-28";

    public static readonly ProductInfoHeaderValue UserAgent = new(
        "Ardea", typeof(GitHubHttp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0");

    public static HttpRequestMessage Get(GitHubHost host, string relativePath, string? token, string accept = "application/vnd.github+json")
    {
        var url = new Uri(host.ApiBase, relativePath);
        if (!IsOnApiHost(host, url))
        {
            throw new ArgumentException("The path leaves the API host of the connection.", nameof(relativePath));
        }

        return Get(host, url, token, accept);
    }

    public static HttpRequestMessage Get(GitHubHost host, Uri url, string? token, string accept = "application/vnd.github+json")
    {
        if (!IsOnApiHost(host, url))
        {
            throw new ArgumentException("The address is not on the API host of the connection.", nameof(url));
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(Platform.Http.OutboundAddresses.AllowInternal, host.Id is not null);
        request.Headers.UserAgent.Add(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    public static bool IsOnApiHost(GitHubHost host, Uri url) =>
        url.IsAbsoluteUri
        && url.Scheme == host.ApiBase.Scheme
        && string.Equals(url.IdnHost, host.ApiBase.IdnHost, StringComparison.OrdinalIgnoreCase)
        && url.Port == host.ApiBase.Port
        && url.UserInfo.Length == 0
        && url.AbsolutePath.StartsWith(host.ApiBase.AbsolutePath, StringComparison.Ordinal);
}
