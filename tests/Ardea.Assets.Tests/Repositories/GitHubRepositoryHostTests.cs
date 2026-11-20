using System.Net;
using System.Text;
using Ardea.Assets.GitHub;
using Ardea.Assets.Repositories;
using Ardea.Assets.Repositories.Packages;
using Microsoft.Extensions.Time.Testing;

namespace Ardea.Assets.Tests.Repositories;

public sealed class GitHubRepositoryHostTests
{
    private const string Sha = "43fca52d28a422c8be48fd489cbd4f2d6152bab6";
    private const string Token = "github_pat_secret";
    private const string Api = "https://api.github.com/repos/expressjs/express";
    private const string Report = Api + "/dependency-graph/sbom/fetch-report/27ee5875-bf73-4f53-b12e-dd30c47a7b2d";
    private const string Download = "https://dgpproduction.blob.core.windows.net/sbom-reports/SbomService-001:1:27ee%2Fexpressjs_express.json?rscd=attachment&sig=signed";

    private static readonly RepositoryRef Express = new(GitHubHost.GitHubCom, Token, "expressjs", "express");

    private readonly FakeGitHub _github = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 19, 6, 0, 0, TimeSpan.Zero));
    private readonly List<TimeSpan> _waits = [];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private GitHubRepositoryHost Host(RepositoryHostLimits? limits = null) => new(_github, _time, limits)
    {
        Delay = (delay, _) =>
        {
            _waits.Add(delay);
            _time.Advance(delay);
            return Task.CompletedTask;
        },
    };

    [Fact]
    public void A_failed_token_check_tells_the_members_the_kind_of_failure_and_not_the_network_details()
    {
        var refused = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (10.0.0.5:443)");
        var blocked = new HttpRequestException("ghe.internal resolves to no address Ardea may connect to.");
        var redirect = new HttpRequestException("GitHub answered with a redirect, which Ardea does not follow for API calls.");

        Assert.Equal("GitHub could not be reached (ConnectionError).", GitHubApi.Describe(refused));
        Assert.Equal("The GitHub server resolves to an address Ardea may not connect to.", GitHubApi.Describe(blocked));
        Assert.Equal(redirect.Message, GitHubApi.Describe(redirect));
    }

    [Fact]
    public async Task Branch_head_returns_the_commit_and_the_etag()
    {
        _github.On($"{Api}/commits/HEAD", _ => Text(HttpStatusCode.OK, Sha, "application/vnd.github.sha", etag: $"\"{Sha}\""));

        var result = await Host().GetBranchHeadAsync(Express, null, Cancel);

        Assert.True(result.Ok);
        Assert.Equal(new BranchHead(false, Sha, $"\"{Sha}\""), result.Value);
        var request = Assert.Single(_github.Requests);
        Assert.Equal("application/vnd.github.sha", request.Accept);
        Assert.Equal(GitHubHttp.ApiVersion, request.ApiVersion);
        Assert.Equal($"Bearer {Token}", request.Authorization);
        Assert.StartsWith("Ardea/", request.UserAgent, StringComparison.Ordinal);
        Assert.Null(request.IfNoneMatch);
    }

    [Fact]
    public async Task Branch_head_unchanged_is_304_without_a_new_sha()
    {
        _github.On($"{Api}/commits/HEAD", r => r.Headers.IfNoneMatch.ToString() == $"\"{Sha}\""
            ? new HttpResponseMessage(HttpStatusCode.NotModified) { Headers = { ETag = new($"\"{Sha}\"") } }
            : Text(HttpStatusCode.OK, Sha));

        var result = await Host().GetBranchHeadAsync(Express, $"\"{Sha}\"", Cancel);

        Assert.Equal(new BranchHead(true, null, $"\"{Sha}\""), result.Value);
        Assert.Equal($"\"{Sha}\"", Assert.Single(_github.Requests).IfNoneMatch);
    }

    [Fact]
    public async Task Branch_with_slash_is_requested_as_path_and_a_missing_one_is_ref_not_found()
    {
        _github.On($"{Api}/commits/feat/fresh-query", _ => Text(HttpStatusCode.UnprocessableEntity, "{\"message\":\"No commit found for SHA: feat/fresh-query\"}"));

        var result = await Host().GetBranchHeadAsync(Express with { Ref = "feat/fresh-query" }, null, Cancel);

        Assert.Equal(RepositoryFailure.RefNotFound, result.Error?.Kind);
    }

    [Theory]
    [InlineData("expressjs/..", "express", null)]
    [InlineData("expressjs", "express?x=1", null)]
    [InlineData("expressjs", "express", "../../users")]
    [InlineData("expressjs", "express", "a b")]
    public async Task Malformed_names_and_refs_send_nothing(string owner, string name, string? reference)
    {
        var result = await Host().GetBranchHeadAsync(Express with { Owner = owner, Name = name, Ref = reference }, null, Cancel);

        Assert.Equal(RepositoryFailure.InvalidRequest, result.Error?.Kind);
        Assert.Empty(_github.Requests);
    }

    [Fact]
    public async Task Rate_limit_is_a_result_with_the_time_to_wait()
    {
        _github.On($"{Api}/commits/HEAD", _ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Headers = { { "x-ratelimit-remaining", "0" }, { "x-ratelimit-reset", _time.GetUtcNow().AddMinutes(20).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture) } },
        });

        var result = await Host().GetBranchHeadAsync(Express, null, Cancel);

        Assert.Equal(RepositoryFailure.RateLimited, result.Error?.Kind);
        Assert.Equal(TimeSpan.FromMinutes(20), result.Error?.RetryAfter);
    }

    [Fact]
    public async Task Sbom_is_requested_polled_and_downloaded_without_the_token()
    {
        _github.On($"{Api}/dependency-graph/sbom/generate-report", _ => Text(HttpStatusCode.Created, $"{{\"sbom_url\":\"{Report}\"}}"));
        var polls = 0;
        _github.On(Report, _ => ++polls == 1
            ? new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{ }"), Headers = { RetryAfter = new(TimeSpan.FromSeconds(3)) } }
            : new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(Download) } });
        _github.On(Download, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Fixture("express-sbom.json")) });

        var result = await Host().GetSbomAsync(Express, SpdxReader.ReadAsync, Cancel);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(53, result.Value!.Findings.Count);
        Assert.Equal([TimeSpan.FromSeconds(3)], _waits);
        Assert.Equal(4, _github.Requests.Count);
        Assert.All(_github.Requests.Take(3), r => Assert.Equal($"Bearer {Token}", r.Authorization));
        var download = _github.Requests[3];
        Assert.Equal("dgpproduction.blob.core.windows.net", download.Url.Host);
        Assert.Null(download.Authorization);
        Assert.Null(download.ApiVersion);
    }

    [Theory]
    [InlineData("https://evil.example.com/sbom.json")]
    [InlineData("http://dgpproduction.blob.core.windows.net/sbom.json")]
    [InlineData("https://dgpproduction.blob.core.windows.net:8443/sbom.json")]
    [InlineData("https://user:pw@dgpproduction.blob.core.windows.net/sbom.json")]
    [InlineData("https://dgpproduction.blob.core.windows.net.evil.example.com/sbom.json")]
    public async Task Sbom_redirect_to_an_undeclared_target_is_refused(string location)
    {
        _github.On($"{Api}/dependency-graph/sbom/generate-report", _ => Text(HttpStatusCode.Created, $"{{\"sbom_url\":\"{Report}\"}}"));
        _github.On(Report, _ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } });

        var result = await Host().GetSbomAsync(Express, SpdxReader.ReadAsync, Cancel);

        Assert.Equal(RepositoryFailure.RedirectRefused, result.Error?.Kind);
        Assert.Equal(2, _github.Requests.Count);
        Assert.DoesNotContain("pw", result.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sbom_report_address_off_the_api_host_is_not_called()
    {
        _github.On($"{Api}/dependency-graph/sbom/generate-report", _ => Text(HttpStatusCode.Created, "{\"sbom_url\":\"https://evil.example.com/repos/expressjs/express/dependency-graph/sbom/fetch-report/1\"}"));

        var result = await Host().GetSbomAsync(Express, SpdxReader.ReadAsync, Cancel);

        Assert.Equal(RepositoryFailure.InvalidResponse, result.Error?.Kind);
        Assert.Single(_github.Requests);
    }

    [Fact]
    public async Task Sbom_404_while_the_repository_answers_means_the_dependency_graph_is_off()
    {
        _github.On($"{Api}/dependency-graph/sbom/generate-report", _ => Text(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}"));
        _github.On(Api, _ => Text(HttpStatusCode.OK, "{\"name\":\"express\"}"));

        var result = await Host().GetSbomAsync(Express, SpdxReader.ReadAsync, Cancel);

        Assert.Equal(RepositoryFailure.DependencyGraphDisabled, result.Error?.Kind);
    }

    [Fact]
    public async Task Sbom_404_with_the_repository_404_is_not_found()
    {
        _github.On($"{Api}/dependency-graph/sbom/generate-report", _ => Text(HttpStatusCode.NotFound, "{}"));
        _github.On(Api, _ => Text(HttpStatusCode.NotFound, "{}"));

        var result = await Host().GetSbomAsync(Express, SpdxReader.ReadAsync, Cancel);

        Assert.Equal(RepositoryFailure.NotFound, result.Error?.Kind);
    }

    [Fact]
    public async Task Sbom_still_pending_after_the_budget_is_not_ready_never_empty()
    {
        _github.On($"{Api}/dependency-graph/sbom/generate-report", _ => Text(HttpStatusCode.Created, $"{{\"sbom_url\":\"{Report}\"}}"));
        _github.On(Report, _ => new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{ }"), Headers = { RetryAfter = new(TimeSpan.FromSeconds(1)) } });

        var result = await Host(new RepositoryHostLimits { MaxSbomPolls = 50, MaxSbomWait = TimeSpan.FromSeconds(30) }).GetSbomAsync(Express, SpdxReader.ReadAsync, Cancel);

        Assert.Equal(RepositoryFailure.SbomNotReady, result.Error?.Kind);
        Assert.Null(result.Value);
        Assert.True(_waits.Sum(w => w.TotalSeconds) <= 30);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], _waits.Select(w => w.TotalSeconds));
    }

    [Fact]
    public async Task Sbom_poll_count_is_bounded()
    {
        _github.On($"{Api}/dependency-graph/sbom/generate-report", _ => Text(HttpStatusCode.Created, $"{{\"sbom_url\":\"{Report}\"}}"));
        _github.On(Report, _ => new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{ }") });

        var result = await Host(new RepositoryHostLimits { MaxSbomPolls = 3 }).GetSbomAsync(Express, SpdxReader.ReadAsync, Cancel);

        Assert.Equal(RepositoryFailure.SbomNotReady, result.Error?.Kind);
        Assert.Equal(1 + 3, _github.Requests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sbom_larger_than_the_limit_is_refused_while_reading(bool announced)
    {
        _github.On($"{Api}/dependency-graph/sbom/generate-report", _ => Text(HttpStatusCode.Created, $"{{\"sbom_url\":\"{Report}\"}}"));
        _github.On(Report, _ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(Download) } });
        _github.On(Download, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = Body(Fixture("express-sbom.json"), announced) });

        var result = await Host(new RepositoryHostLimits { MaxSbomBytes = 10_000 }).GetSbomAsync(Express, SpdxReader.ReadAsync, Cancel);

        Assert.Equal(RepositoryFailure.TooLarge, result.Error?.Kind);
    }

    [Fact]
    public async Task Tree_lists_files_in_the_subfolder_and_reports_truncation()
    {
        _github.On($"{Api}/git/trees/{Sha}?recursive=1", _ => Text(HttpStatusCode.OK, $$"""
            {
              "sha": "{{Sha}}", "truncated": true,
              "tree": [
                { "path": "package.json", "mode": "100644", "type": "blob", "size": 2731 },
                { "path": "web", "mode": "040000", "type": "tree" },
                { "path": "web/package.json", "mode": "100644", "type": "blob", "size": 100 },
                { "path": "web/package-lock.json", "mode": "100644", "type": "blob", "size": 447180 },
                { "path": "web/link.json", "mode": "120000", "type": "blob", "size": 12 },
                { "path": "web/vendor", "mode": "160000", "type": "commit" },
                { "path": "webapp/package.json", "mode": "100644", "type": "blob", "size": 50 }
              ]
            }
            """));

        var result = await Host().GetTreeAsync(Express with { Subfolder = "/web/" }, Sha, Cancel);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.True(result.Value!.Truncated);
        Assert.Equal([new RepositoryFile("web/package.json", 100), new RepositoryFile("web/package-lock.json", 447180)], result.Value.Files);
    }

    [Fact]
    public async Task Tree_beyond_the_entry_limit_is_truncated()
    {
        var entries = string.Join(',', Enumerable.Range(0, 10).Select(i => $"{{\"path\":\"f{i}.json\",\"mode\":\"100644\",\"type\":\"blob\",\"size\":1}}"));
        _github.On($"{Api}/git/trees/{Sha}?recursive=1", _ => Text(HttpStatusCode.OK, $"{{\"sha\":\"{Sha}\",\"truncated\":false,\"tree\":[{entries}]}}"));

        var result = await Host(new RepositoryHostLimits { MaxTreeEntries = 4 }).GetTreeAsync(Express, Sha, Cancel);

        Assert.Equal(4, result.Value!.Files.Count);
        Assert.True(result.Value.Truncated);
    }

    [Fact]
    public async Task Tree_needs_a_full_commit_sha()
    {
        var result = await Host().GetTreeAsync(Express, "main", Cancel);

        Assert.Equal(RepositoryFailure.InvalidRequest, result.Error?.Kind);
        Assert.Empty(_github.Requests);
    }

    [Fact]
    public async Task File_is_read_raw_at_the_commit()
    {
        _github.On($"{Api}/contents/web/package.json?ref={Sha}", _ => Text(HttpStatusCode.OK, "{\"name\":\"web\"}", "application/vnd.github.raw"));

        var result = await Host().GetFileAsync(Express, Sha, "web/package.json", 1024, Cancel);

        Assert.Equal("{\"name\":\"web\"}", Encoding.UTF8.GetString(result.Value!));
        Assert.Equal("application/vnd.github.raw", Assert.Single(_github.Requests).Accept);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task File_larger_than_the_limit_is_refused_while_reading(bool announced)
    {
        _github.On($"{Api}/contents/package-lock.json?ref={Sha}", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = Body(new byte[5000], announced) });

        var result = await Host().GetFileAsync(Express, Sha, "package-lock.json", 4096, Cancel);

        Assert.Equal(RepositoryFailure.TooLarge, result.Error?.Kind);
    }

    [Fact]
    public async Task Directory_is_not_a_file()
    {
        _github.On($"{Api}/contents/lib?ref={Sha}", _ => Text(HttpStatusCode.OK, "[{\"name\":\"a.js\"}]", "application/json"));

        var result = await Host().GetFileAsync(Express, Sha, "lib", 4096, Cancel);

        Assert.Equal(RepositoryFailure.NotAFile, result.Error?.Kind);
    }

    [Theory]
    [InlineData("../../../user")]
    [InlineData("/etc/passwd")]
    [InlineData("a/./b")]
    public async Task File_paths_are_checked_not_resolved(string path)
    {
        var result = await Host().GetFileAsync(Express, Sha, path, 4096, Cancel);

        Assert.Equal(RepositoryFailure.InvalidRequest, result.Error?.Kind);
        Assert.Empty(_github.Requests);
    }

    [Fact]
    public async Task Network_failure_is_a_result()
    {
        _github.On($"{Api}/commits/HEAD", _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "connection refused"));

        var result = await Host().GetBranchHeadAsync(Express, null, Cancel);

        Assert.Equal(RepositoryFailure.Unavailable, result.Error?.Kind);
    }

    [Fact]
    public void Repository_never_prints_its_token() =>
        Assert.DoesNotContain(Token, Express.ToString(), StringComparison.Ordinal);

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static HttpResponseMessage Text(HttpStatusCode status, string body, string mediaType = "application/json", string? etag = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
        if (etag is not null)
        {
            response.Headers.ETag = new(etag);
        }

        return response;
    }

    private static HttpContent Body(byte[] bytes, bool announced) =>
        announced ? new ByteArrayContent(bytes) : new StreamContent(new UnseekableStream(bytes));

    private sealed record SeenRequest(Uri Url, string? Authorization, string? Accept, string? ApiVersion, string UserAgent, string? IfNoneMatch);

    private sealed class FakeGitHub : HttpMessageHandler, IHttpClientFactory
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

        public List<SeenRequest> Requests { get; } = [];

        public void On(string url, Func<HttpRequestMessage, HttpResponseMessage> answer) => _routes[new Uri(url).AbsoluteUri] = answer;

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SeenRequest(
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.Accept.ToString() is { Length: > 0 } accept ? accept : null,
                request.Headers.TryGetValues("X-GitHub-Api-Version", out var version) ? version.Single() : null,
                request.Headers.UserAgent.ToString(),
                request.Headers.IfNoneMatch.ToString() is { Length: > 0 } etag ? etag : null));
            return Task.FromResult(_routes.TryGetValue(request.RequestUri!.AbsoluteUri, out var answer) ? answer(request) : new HttpResponseMessage((HttpStatusCode)599));
        }
    }

    private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
