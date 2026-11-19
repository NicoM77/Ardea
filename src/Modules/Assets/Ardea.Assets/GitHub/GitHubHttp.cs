using System.Net.Http.Headers;
using System.Reflection;

namespace Ardea.Assets.GitHub;

/// <summary>
/// How every request to a GitHub API host is built (master plan 16.9, ADR 032): the path is relative
/// to the connection's API base, so only that host is reached; the API version is pinned; the token
/// goes only to that host; an Enterprise Server may resolve to an internal address because the
/// instance administrator registered it.
/// </summary>
internal static class GitHubHttp
{
    /// <summary>The REST API version Ardea was checked against.</summary>
    public const string ApiVersion = "2022-11-28";

    public static readonly ProductInfoHeaderValue UserAgent = new(
        "Ardea", typeof(GitHubHttp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0");

    /// <summary>A GET to <paramref name="relativePath"/> under the API base of <paramref name="host"/>.</summary>
    public static HttpRequestMessage Get(GitHubHost host, string relativePath, string? token, string accept = "application/vnd.github+json")
    {
        var url = new Uri(host.ApiBase, relativePath);
        if (!IsOnApiHost(host, url))
        {
            throw new ArgumentException("The path leaves the API host of the connection.", nameof(relativePath));
        }

        return Get(host, url, token, accept);
    }

    /// <summary>A GET to an absolute <paramref name="url"/> that must lie under the API base of <paramref name="host"/>.</summary>
    public static HttpRequestMessage Get(GitHubHost host, Uri url, string? token, string accept = "application/vnd.github+json")
    {
        if (!IsOnApiHost(host, url))
        {
            throw new ArgumentException("The address is not on the API host of the connection.", nameof(url));
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        // An Enterprise Server is the administrator's choice and may live in the internal network (16.9).
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

    /// <summary>Whether <paramref name="url"/> is on the scheme, host and port of the API base and under its path.</summary>
    public static bool IsOnApiHost(GitHubHost host, Uri url) =>
        url.IsAbsoluteUri
        && url.Scheme == host.ApiBase.Scheme
        && string.Equals(url.IdnHost, host.ApiBase.IdnHost, StringComparison.OrdinalIgnoreCase)
        && url.Port == host.ApiBase.Port
        && url.UserInfo.Length == 0
        && url.AbsolutePath.StartsWith(host.ApiBase.AbsolutePath, StringComparison.Ordinal);
}
