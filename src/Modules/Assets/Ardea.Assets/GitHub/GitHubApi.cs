using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ardea.Assets.GitHub;

/// <summary>Where a connection talks to: github.com, or a GitHub Enterprise Server the instance administrator registered.</summary>
internal sealed record GitHubHost(string? Id, Uri ApiBase)
{
    public static readonly GitHubHost GitHubCom = new(null, new Uri("https://api.github.com/"));
}

/// <summary>What a token is and may do, as GitHub reports it.</summary>
internal sealed record TokenCheck(bool Valid, string Kind, string? Login, DateTimeOffset? ExpiresAt, IReadOnlyList<string> ClassicScopes, string? Error);

internal sealed record RepositoryInfo(string Owner, string Name, bool Private, string DefaultBranch, DateTimeOffset? PushedAt);

/// <summary>
/// The few GitHub REST calls the connection management needs (master plan 16.4; task 2.10). Only
/// the host of the connection is reachable, no redirect is followed, the API version is pinned
/// (ADR 032) and the token goes nowhere else.
/// </summary>
internal sealed class GitHubApi(IHttpClientFactory clients)
{
    public const string ClientName = "ardea-github";

    /// <summary>The REST API version Ardea was checked against.</summary>
    public const string ApiVersion = GitHubHttp.ApiVersion;

    private const int MaxRepositoryPages = 10;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Checks a token with <c>GET /user</c>. Fine-grained tokens (<c>github_pat_</c>) report their
    /// expiry in a header; classic tokens (<c>ghp_</c>) report their scopes, which are shown as a warning.
    /// </summary>
    public async Task<TokenCheck> CheckTokenAsync(GitHubHost host, string token, CancellationToken cancellationToken)
    {
        var kind = token.StartsWith("github_pat_", StringComparison.Ordinal) ? "fine_grained"
            : token.StartsWith("ghp_", StringComparison.Ordinal) ? "classic" : "other";
        try
        {
            using var response = await SendAsync(host, "user", token, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized)
            {
                return new TokenCheck(false, kind, null, null, [], "GitHub does not accept this token.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new TokenCheck(false, kind, null, null, [], $"GitHub answered {(int)response.StatusCode}.");
            }

            var user = await response.Content.ReadFromJsonAsync<UserResponse>(Json, cancellationToken);
            var scopes = response.Headers.TryGetValues("X-OAuth-Scopes", out var values)
                ? values.SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).ToList()
                : [];
            return new TokenCheck(true, kind, user?.Login, Expiry(response), scopes, null);
        }
        catch (HttpRequestException ex)
        {
            return new TokenCheck(false, kind, null, null, [], ex.Message);
        }
    }

    /// <summary>
    /// Repositories the token can read, most recently pushed first, filtered by <paramref name="query"/>.
    /// Without a token only one public repository can be found, by <c>owner/name</c>.
    /// </summary>
    public async Task<IReadOnlyList<RepositoryInfo>> RepositoriesAsync(GitHubHost host, string? token, string? query, CancellationToken cancellationToken)
    {
        query = query?.Trim();
        if (token is null)
        {
            if (query is null || query.Split('/') is not [{ Length: > 0 } owner, { Length: > 0 } name])
            {
                return [];
            }

            using var single = await SendAsync(host, $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}", null, cancellationToken);
            return single.IsSuccessStatusCode && await single.Content.ReadFromJsonAsync<RepositoryResponse>(Json, cancellationToken) is { Private: false } found
                ? [found.ToInfo()]
                : [];
        }

        var result = new List<RepositoryInfo>();
        for (var page = 1; page <= MaxRepositoryPages; page++)
        {
            using var response = await SendAsync(host, $"user/repos?per_page=100&sort=pushed&page={page}", token, cancellationToken);
            response.EnsureSuccessStatusCode();
            var repositories = await response.Content.ReadFromJsonAsync<List<RepositoryResponse>>(Json, cancellationToken) ?? [];
            result.AddRange(repositories.Select(r => r.ToInfo()));
            if (repositories.Count < 100)
            {
                break;
            }
        }

        return [.. result.Where(r => string.IsNullOrEmpty(query) || $"{r.Owner}/{r.Name}".Contains(query, StringComparison.OrdinalIgnoreCase)).Take(100)];
    }

    private async Task<HttpResponseMessage> SendAsync(GitHubHost host, string path, string? token, CancellationToken cancellationToken)
    {
        using var request = GitHubHttp.Get(host, path, token);
        var response = await clients.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            response.Dispose();
            throw new HttpRequestException("GitHub answered with a redirect, which Ardea does not follow for API calls.");
        }

        return response;
    }

    /// <summary><c>github-authentication-token-expiration: 2026-12-31 00:00:00 UTC</c>.</summary>
    private static DateTimeOffset? Expiry(HttpResponseMessage response) =>
        response.Headers.TryGetValues("github-authentication-token-expiration", out var values)
        && DateTimeOffset.TryParse(values.First().Replace(" UTC", "Z", StringComparison.Ordinal), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires)
            ? expires
            : null;

    private sealed record UserResponse(string Login);

    private sealed record OwnerResponse(string Login);

    private sealed record RepositoryResponse(
        string Name,
        OwnerResponse Owner,
        bool Private,
        [property: JsonPropertyName("default_branch")] string? DefaultBranch,
        [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt)
    {
        public RepositoryInfo ToInfo() => new(Owner.Login, Name, Private, DefaultBranch ?? "main", PushedAt);
    }
}
