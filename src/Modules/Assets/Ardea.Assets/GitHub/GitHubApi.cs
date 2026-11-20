using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ardea.Assets.GitHub;

internal sealed record GitHubHost(string? Id, Uri ApiBase)
{
    public static readonly GitHubHost GitHubCom = new(null, new Uri("https://api.github.com/"));
}

internal sealed record TokenCheck(bool Valid, string Kind, string? Login, DateTimeOffset? ExpiresAt, IReadOnlyList<string> ClassicScopes, string? Error);

internal sealed record RepositoryInfo(string Owner, string Name, bool Private, string DefaultBranch, DateTimeOffset? PushedAt);

internal sealed class GitHubApi(IHttpClientFactory clients)
{
    public const string ClientName = "ardea-github";

    public const string ApiVersion = GitHubHttp.ApiVersion;

    private const int MaxRepositoryPages = 10;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
            return new TokenCheck(false, kind, null, null, [], Describe(ex));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TokenCheck(false, kind, null, null, [], "GitHub did not answer in time.");
        }
        catch (JsonException)
        {
            return new TokenCheck(false, kind, null, null, [], "GitHub's answer could not be read.");
        }
    }

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

    internal static string Describe(HttpRequestException exception) =>
        exception.Message.StartsWith("GitHub ", StringComparison.Ordinal) ? exception.Message
        : exception.Message.Contains("resolves to no address Ardea may connect to", StringComparison.Ordinal) ? "The GitHub server resolves to an address Ardea may not connect to."
        : $"GitHub could not be reached ({exception.HttpRequestError}).";

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
