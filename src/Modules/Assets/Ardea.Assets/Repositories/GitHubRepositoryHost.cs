using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ardea.Assets.GitHub;
using Ardea.Platform.Http;

namespace Ardea.Assets.Repositories;

/// <summary>Bounds of one reading (master plan 16.3, 16.9; ADR 032).</summary>
internal sealed record RepositoryHostLimits
{
    public static readonly RepositoryHostLimits Default = new();

    /// <summary>ADR 032, decision 2.</summary>
    public long MaxSbomBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Requests to <c>fetch-report</c> per call; GitHub counts them against the <c>dependency_sbom</c> bucket of 100 per hour.</summary>
    public int MaxSbomPolls { get; init; } = 20;

    /// <summary>How long one call waits for GitHub to build the SBOM before it answers <see cref="RepositoryFailure.SbomNotReady"/>.</summary>
    public TimeSpan MaxSbomWait { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>The longest single wait between two polls, whatever <c>Retry-After</c> says.</summary>
    public TimeSpan MaxSbomPollDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The download of the finished SBOM, beyond the client's timeout for the response headers.</summary>
    public TimeSpan SbomDownloadTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The recursive tree; GitHub itself cuts at 100 000 entries and about 7 MB.</summary>
    public long MaxTreeBytes { get; init; } = 32L * 1024 * 1024;

    /// <summary>Files listed per tree, counted after the subfolder filter.</summary>
    public int MaxTreeEntries { get; init; } = 50_000;
}

/// <summary>
/// <see cref="IRepositoryHost"/> for github.com and GitHub Enterprise Server (master plan 16.4, ADR 032).
/// Every request goes through the <c>ardea-github</c> client, which checks the addresses and follows
/// no redirect. Only two kinds of hosts are reached: the API host of the connection, and for the SBOM
/// the declared download hosts, which never see the token.
/// </summary>
internal sealed class GitHubRepositoryHost(IHttpClientFactory clients, TimeProvider time, RepositoryHostLimits? limits = null) : IRepositoryHost
{
    /// <summary>Where github.com redirects for the finished SBOM (ADR 032).</summary>
    public const string GitHubComSbomHost = "dgpproduction.blob.core.windows.net";

    private const int MaxSmallResponseBytes = 64 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };

    private readonly RepositoryHostLimits _limits = limits ?? RepositoryHostLimits.Default;

    /// <summary>The wait between two polls; replaced in tests.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = (delay, cancellationToken) => Task.Delay(delay, time, cancellationToken);

    /// <summary>
    /// The hosts the SBOM may be downloaded from. For an Enterprise Server only the server itself: where
    /// its export redirects is unverified (master plan 16.4), so anything else is refused until it is known.
    /// </summary>
    public static IReadOnlyList<string> SbomDownloadHosts(GitHubHost host) =>
        host.Id is null ? [GitHubComSbomHost] : [host.ApiBase.IdnHost];

    public async Task<RepositoryResult<BranchHead>> GetBranchHeadAsync(RepositoryRef repository, string? etag, CancellationToken cancellationToken)
    {
        if (Check(repository) is { } invalid)
        {
            return RepositoryResult<BranchHead>.Fail(invalid);
        }

        // commits/{ref} accepts branch names with slashes and HEAD for the default branch, and answers
        // with an ETag; with this media type the body is only the SHA.
        var reference = repository.Ref is null ? "HEAD" : RepositoryPaths.Escape(repository.Ref);
        using var request = GitHubHttp.Get(repository.Host, $"{RepositoryPath(repository)}/commits/{reference}", repository.Token, "application/vnd.github.sha");
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        var (response, error) = await SendAsync(request, cancellationToken);
        using (response)
        {
            if (response is null)
            {
                return RepositoryResult<BranchHead>.Fail(error!);
            }

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotModified:
                    return RepositoryResult<BranchHead>.Success(new BranchHead(true, null, response.Headers.ETag?.ToString() ?? etag));
                case HttpStatusCode.OK:
                    var body = await ReadSmallAsync(response, cancellationToken);
                    var sha = body?.Trim();
                    return RepositoryPaths.IsCommitSha(sha)
                        ? RepositoryResult<BranchHead>.Success(new BranchHead(false, sha, response.Headers.ETag?.ToString()))
                        : RepositoryResult<BranchHead>.Fail(RepositoryFailure.InvalidResponse, "GitHub answered the branch head without a commit SHA.");
                case HttpStatusCode.UnprocessableEntity:
                    return RepositoryResult<BranchHead>.Fail(RepositoryFailure.RefNotFound, $"The branch {repository.Ref ?? "HEAD"} does not exist.");
                default:
                    return RepositoryResult<BranchHead>.Fail(Failure(response));
            }
        }
    }

    public async Task<RepositoryResult<T>> GetSbomAsync<T>(RepositoryRef repository, Func<Stream, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        if (Check(repository) is { } invalid)
        {
            return RepositoryResult<T>.Fail(invalid);
        }

        var requested = await RequestSbomAsync(repository, cancellationToken);
        if (requested.Error is not null)
        {
            return RepositoryResult<T>.Fail(requested.Error);
        }

        var started = time.GetTimestamp();
        for (var attempt = 1; attempt <= _limits.MaxSbomPolls; attempt++)
        {
            using var request = GitHubHttp.Get(repository.Host, requested.Value!, repository.Token);
            var (response, error) = await SendAsync(request, cancellationToken);
            using (response)
            {
                if (response is null)
                {
                    return RepositoryResult<T>.Fail(error!);
                }

                switch ((int)response.StatusCode)
                {
                    case 202:
                        // Still being built: "not yet", never an empty SBOM (ADR 032, decision 1). Retry-After
                        // is honoured; the waits grow so a slow export does not use up the bucket.
                        var delay = TimeSpan.FromSeconds(Math.Max(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 1, attempt));
                        delay = delay > _limits.MaxSbomPollDelay ? _limits.MaxSbomPollDelay : delay;
                        if (attempt == _limits.MaxSbomPolls || time.GetElapsedTime(started) + delay > _limits.MaxSbomWait)
                        {
                            return RepositoryResult<T>.Fail(RepositoryFailure.SbomNotReady, "GitHub is still building the SBOM.", delay);
                        }

                        await Delay(delay, cancellationToken);
                        continue;
                    case 301 or 302 or 303 or 307 or 308:
                        return await DownloadSbomAsync(repository.Host, response.Headers.Location, read, cancellationToken);
                    case 200:
                        // Not seen on github.com, but harmless: the SBOM straight from the API host.
                        return await ReadSbomAsync(response, read, cancellationToken);
                    default:
                        return RepositoryResult<T>.Fail(Failure(response));
                }
            }
        }

        return RepositoryResult<T>.Fail(RepositoryFailure.SbomNotReady, "GitHub is still building the SBOM.");
    }

    public async Task<RepositoryResult<RepositoryTree>> GetTreeAsync(RepositoryRef repository, string commitSha, CancellationToken cancellationToken)
    {
        if (Check(repository) is { } invalid)
        {
            return RepositoryResult<RepositoryTree>.Fail(invalid);
        }

        if (!RepositoryPaths.IsCommitSha(commitSha))
        {
            return RepositoryResult<RepositoryTree>.Fail(RepositoryFailure.InvalidRequest, "A tree is read for a full commit SHA.");
        }

        RepositoryPaths.TryNormalizeFolder(repository.Subfolder, out var folder);
        using var request = GitHubHttp.Get(repository.Host, $"{RepositoryPath(repository)}/git/trees/{commitSha}?recursive=1", repository.Token);
        var (response, error) = await SendAsync(request, cancellationToken);
        using (response)
        {
            if (response is null)
            {
                return RepositoryResult<RepositoryTree>.Fail(error!);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return RepositoryResult<RepositoryTree>.Fail(Failure(response));
            }

            if (response.Content.Headers.ContentLength > _limits.MaxTreeBytes)
            {
                return RepositoryResult<RepositoryTree>.Fail(RepositoryFailure.TooLarge, $"The tree is larger than {_limits.MaxTreeBytes} bytes.");
            }

            TreeResponse? tree;
            try
            {
                await using var body = new BoundedStream(await response.Content.ReadAsStreamAsync(cancellationToken), _limits.MaxTreeBytes);
                tree = await JsonSerializer.DeserializeAsync<TreeResponse>(body, Json, cancellationToken);
            }
            catch (ContentTooLargeException)
            {
                return RepositoryResult<RepositoryTree>.Fail(RepositoryFailure.TooLarge, $"The tree is larger than {_limits.MaxTreeBytes} bytes.");
            }
            catch (JsonException)
            {
                return RepositoryResult<RepositoryTree>.Fail(RepositoryFailure.InvalidResponse, "GitHub answered the tree with malformed JSON.");
            }
            catch (IOException)
            {
                return RepositoryResult<RepositoryTree>.Fail(RepositoryFailure.Unavailable, "The tree could not be read to the end.");
            }

            if (tree?.Tree is null)
            {
                return RepositoryResult<RepositoryTree>.Fail(RepositoryFailure.InvalidResponse, "GitHub answered without a tree.");
            }

            var files = new List<RepositoryFile>();
            var truncated = tree.Truncated;
            foreach (var entry in tree.Tree)
            {
                // Files only: no directories, no submodules ("commit") and no symbolic links (mode 120000),
                // whose target Ardea would otherwise have to resolve.
                if (entry.Type != "blob" || entry.Mode == "120000" || !RepositoryPaths.IsValidPath(entry.Path) || !RepositoryPaths.IsWithin(entry.Path, folder))
                {
                    continue;
                }

                if (files.Count == _limits.MaxTreeEntries)
                {
                    truncated = true;
                    break;
                }

                files.Add(new RepositoryFile(entry.Path, entry.Size ?? 0));
            }

            return RepositoryResult<RepositoryTree>.Success(new RepositoryTree(commitSha, files, truncated));
        }
    }

    public async Task<RepositoryResult<byte[]>> GetFileAsync(RepositoryRef repository, string commitSha, string path, int maxBytes, CancellationToken cancellationToken)
    {
        if (Check(repository) is { } invalid)
        {
            return RepositoryResult<byte[]>.Fail(invalid);
        }

        if (!RepositoryPaths.IsCommitSha(commitSha) || !RepositoryPaths.IsValidPath(path) || maxBytes <= 0)
        {
            return RepositoryResult<byte[]>.Fail(RepositoryFailure.InvalidRequest, "A file is read by a full commit SHA, a relative path and a positive limit.");
        }

        using var request = GitHubHttp.Get(
            repository.Host, $"{RepositoryPath(repository)}/contents/{RepositoryPaths.Escape(path)}?ref={commitSha}", repository.Token, "application/vnd.github.raw");
        var (response, error) = await SendAsync(request, cancellationToken);
        using (response)
        {
            if (response is null)
            {
                return RepositoryResult<byte[]>.Fail(error!);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return RepositoryResult<byte[]>.Fail(Failure(response));
            }

            // For a directory GitHub ignores the raw media type and lists the entries as JSON.
            if (response.Content.Headers.ContentType?.MediaType == "application/json")
            {
                return RepositoryResult<byte[]>.Fail(RepositoryFailure.NotAFile, $"{path} is not a file.");
            }

            if (response.Content.Headers.ContentLength > maxBytes)
            {
                return RepositoryResult<byte[]>.Fail(RepositoryFailure.TooLarge, $"{path} is larger than {maxBytes} bytes.");
            }

            try
            {
                var content = await BoundedStream.ReadAllAsync(await response.Content.ReadAsStreamAsync(cancellationToken), maxBytes, cancellationToken);
                return content is null
                    ? RepositoryResult<byte[]>.Fail(RepositoryFailure.TooLarge, $"{path} is larger than {maxBytes} bytes.")
                    : RepositoryResult<byte[]>.Success(content);
            }
            catch (IOException)
            {
                return RepositoryResult<byte[]>.Fail(RepositoryFailure.Unavailable, $"{path} could not be read to the end.");
            }
        }
    }

    /// <summary><c>generate-report</c>: the address to poll, which must lie on the API host under this repository.</summary>
    private async Task<RepositoryResult<Uri>> RequestSbomAsync(RepositoryRef repository, CancellationToken cancellationToken)
    {
        var basePath = $"{RepositoryPath(repository)}/dependency-graph/sbom";
        using var request = GitHubHttp.Get(repository.Host, $"{basePath}/generate-report", repository.Token);
        var (response, error) = await SendAsync(request, cancellationToken);
        using (response)
        {
            if (response is null)
            {
                return RepositoryResult<Uri>.Fail(error!);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return await RepositoryVisibleAsync(repository, cancellationToken) is { } repositoryError
                    ? RepositoryResult<Uri>.Fail(repositoryError)
                    : RepositoryResult<Uri>.Fail(RepositoryFailure.DependencyGraphDisabled, "The dependency graph is not enabled for this repository.");
            }

            if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Accepted))
            {
                return RepositoryResult<Uri>.Fail(Failure(response));
            }

            var body = await ReadSmallAsync(response, cancellationToken);
            string? url = null;
            try
            {
                url = body is null ? null : JsonSerializer.Deserialize<GenerateReportResponse>(body, Json)?.SbomUrl;
            }
            catch (JsonException)
            {
            }

            var expected = new Uri(repository.Host.ApiBase, $"{basePath}/fetch-report/").AbsolutePath;
            return Uri.TryCreate(url, UriKind.Absolute, out var report)
                && GitHubHttp.IsOnApiHost(repository.Host, report)
                && report.AbsolutePath.StartsWith(expected, StringComparison.OrdinalIgnoreCase)
                && report.AbsolutePath.Length > expected.Length
                    ? RepositoryResult<Uri>.Success(report)
                    : RepositoryResult<Uri>.Fail(RepositoryFailure.InvalidResponse, "GitHub answered the SBOM request without a report address on its API host.");
        }
    }

    /// <summary><c>null</c> when <c>GET /repos/{owner}/{repo}</c> answers 200, otherwise why not.</summary>
    private async Task<RepositoryError?> RepositoryVisibleAsync(RepositoryRef repository, CancellationToken cancellationToken)
    {
        using var request = GitHubHttp.Get(repository.Host, RepositoryPath(repository), repository.Token);
        var (response, error) = await SendAsync(request, cancellationToken);
        using (response)
        {
            return response is null ? error : response.StatusCode == HttpStatusCode.OK ? null : Failure(response);
        }
    }

    /// <summary>
    /// Follows the redirect of <c>fetch-report</c> by hand: HTTPS, a declared host, no credentials in the
    /// address, and no <c>Authorization</c> header (ADR 032). The signed address is never put in a message.
    /// </summary>
    private async Task<RepositoryResult<T>> DownloadSbomAsync<T>(GitHubHost host, Uri? location, Func<Stream, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        if (location is null || !location.IsAbsoluteUri)
        {
            return RepositoryResult<T>.Fail(RepositoryFailure.InvalidResponse, "GitHub redirected without an absolute address.");
        }

        if (location.Scheme != Uri.UriSchemeHttps || !location.IsDefaultPort || location.UserInfo.Length > 0
            || !SbomDownloadHosts(host).Contains(location.IdnHost, StringComparer.OrdinalIgnoreCase))
        {
            return RepositoryResult<T>.Fail(RepositoryFailure.RedirectRefused, $"GitHub redirected the SBOM to {location.Scheme}://{location.IdnHost}, which is not a declared download host.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, location);
        request.Options.Set(OutboundAddresses.AllowInternal, host.Id is not null);
        request.Headers.UserAgent.Add(GitHubHttp.UserAgent);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_limits.SbomDownloadTimeout);
        try
        {
            var (response, error) = await SendAsync(request, timeout.Token);
            using (response)
            {
                if (response is null)
                {
                    return RepositoryResult<T>.Fail(error!);
                }

                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    return RepositoryResult<T>.Fail(RepositoryFailure.RedirectRefused, "The download host redirected again, which Ardea does not follow.");
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return RepositoryResult<T>.Fail(
                        (int)response.StatusCode >= 500 ? RepositoryFailure.Unavailable : RepositoryFailure.InvalidResponse,
                        $"The download host answered {(int)response.StatusCode}.");
                }

                return await ReadSbomAsync(response, read, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RepositoryResult<T>.Fail(RepositoryFailure.Unavailable, "The SBOM download timed out.");
        }
    }

    private async Task<RepositoryResult<T>> ReadSbomAsync<T>(HttpResponseMessage response, Func<Stream, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > _limits.MaxSbomBytes)
        {
            return RepositoryResult<T>.Fail(RepositoryFailure.TooLarge, $"The SBOM is larger than {_limits.MaxSbomBytes} bytes.");
        }

        try
        {
            await using var body = new BoundedStream(await response.Content.ReadAsStreamAsync(cancellationToken), _limits.MaxSbomBytes);
            return RepositoryResult<T>.Success(await read(body, cancellationToken));
        }
        catch (ContentTooLargeException)
        {
            return RepositoryResult<T>.Fail(RepositoryFailure.TooLarge, $"The SBOM is larger than {_limits.MaxSbomBytes} bytes.");
        }
        catch (IOException)
        {
            return RepositoryResult<T>.Fail(RepositoryFailure.Unavailable, "The SBOM could not be read to the end.");
        }
    }

    private async Task<(HttpResponseMessage? Response, RepositoryError? Error)> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return (await clients.CreateClient(GitHubApi.ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken), null);
        }
        catch (HttpRequestException ex)
        {
            // The message would name the address, which for the download carries a signature.
            return (null, new RepositoryError(RepositoryFailure.Unavailable, $"{request.RequestUri?.IdnHost} could not be reached ({ex.HttpRequestError})."));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, new RepositoryError(RepositoryFailure.Unavailable, $"{request.RequestUri?.IdnHost} did not answer in time."));
        }
    }

    /// <summary>A failure for an answer that is not the expected one.</summary>
    private RepositoryError Failure(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (status is 403 or 429 && RateLimit(response) is { } wait)
        {
            return new RepositoryError(RepositoryFailure.RateLimited, "GitHub's rate limit is reached.", wait);
        }

        return status switch
        {
            401 => new RepositoryError(RepositoryFailure.Unauthorized, "GitHub does not accept the token."),
            403 => new RepositoryError(RepositoryFailure.Forbidden, "The token may not read this repository."),
            404 => new RepositoryError(RepositoryFailure.NotFound, "Not found, or not visible with this access."),
            409 => new RepositoryError(RepositoryFailure.EmptyRepository, "The repository has no commit yet."),
            429 => new RepositoryError(RepositoryFailure.RateLimited, "GitHub's rate limit is reached.", TimeSpan.FromMinutes(1)),
            >= 300 and < 400 => new RepositoryError(RepositoryFailure.RedirectRefused, "GitHub redirected; the repository may have moved or been renamed."),
            >= 500 => new RepositoryError(RepositoryFailure.Unavailable, $"GitHub answered {status}."),
            _ => new RepositoryError(RepositoryFailure.InvalidResponse, $"GitHub answered {status}."),
        };
    }

    /// <summary>
    /// How long a rate limit lasts: <c>Retry-After</c> for a secondary limit, the reset time when
    /// <c>x-ratelimit-remaining</c> is 0; <c>null</c> when the 403 is no rate limit.
    /// </summary>
    private TimeSpan? RateLimit(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { } retryAfter)
        {
            return retryAfter.Delta ?? (retryAfter.Date is { } date ? Max(date - time.GetUtcNow()) : TimeSpan.FromMinutes(1));
        }

        if (Header(response, "x-ratelimit-remaining") == "0")
        {
            return long.TryParse(Header(response, "x-ratelimit-reset"), NumberStyles.None, CultureInfo.InvariantCulture, out var reset)
                ? Max(DateTimeOffset.FromUnixTimeSeconds(reset) - time.GetUtcNow())
                : TimeSpan.FromMinutes(1);
        }

        return null;

        static TimeSpan Max(TimeSpan wait) => wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static async Task<string?> ReadSmallAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await BoundedStream.ReadAllAsync(await response.Content.ReadAsStreamAsync(cancellationToken), MaxSmallResponseBytes, cancellationToken);
            return bytes is null ? null : Encoding.UTF8.GetString(bytes);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static RepositoryError? Check(RepositoryRef repository) =>
        !RepositoryPaths.IsValidName(repository.Owner) || !RepositoryPaths.IsValidName(repository.Name)
            ? new RepositoryError(RepositoryFailure.InvalidRequest, "Owner and name of the repository are malformed.")
            : repository.Ref is not null && !RepositoryPaths.IsValidRef(repository.Ref)
                ? new RepositoryError(RepositoryFailure.InvalidRequest, "The branch name is malformed.")
                : !RepositoryPaths.TryNormalizeFolder(repository.Subfolder, out _)
                    ? new RepositoryError(RepositoryFailure.InvalidRequest, "The subfolder is malformed.")
                    : null;

    private static string RepositoryPath(RepositoryRef repository) =>
        $"repos/{Uri.EscapeDataString(repository.Owner)}/{Uri.EscapeDataString(repository.Name)}";

    private sealed record GenerateReportResponse([property: JsonPropertyName("sbom_url")] string? SbomUrl);

    private sealed record TreeResponse(string? Sha, List<TreeEntry>? Tree, bool Truncated);

    private sealed record TreeEntry(string? Path, string? Mode, string? Type, long? Size);
}
