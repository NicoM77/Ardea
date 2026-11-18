using System.Net;
using Ardea.Intel.Http;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Tests;

/// <summary>The platform's HTTP client for adapters (task 1.3, master plan 5.2, 9.5).</summary>
public sealed class SourceHttpTests
{
    private static readonly SourceDescriptor Source = new()
    {
        Key = "test",
        Name = "Test",
        Kind = SourceKind.Record,
        Operator = "Tests",
        OperatorKind = OperatorKind.Community,
        DefaultInterval = TimeSpan.FromHours(1),
        AllowedHosts = ["data.example.org", "mirror.example.org"],
        Attribution = "Test",
        RequestsPerMinute = 600,
    };

    [Fact]
    public async Task Undeclared_host_is_refused_before_any_request()
    {
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Client(inner);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("https://evil.example.com/x")));
        Assert.Empty(inner.Requests);
    }

    [Fact]
    public async Task Plain_http_is_refused()
    {
        using var client = Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("http://data.example.org/x")));
    }

    [Fact]
    public async Task Redirect_to_a_declared_host_is_followed_but_not_to_another()
    {
        var inner = new StubHandler(request => request.RequestUri!.Host switch
        {
            "data.example.org" when request.RequestUri.AbsolutePath == "/moved" => Redirect("https://mirror.example.org/file"),
            "data.example.org" => Redirect("https://169.254.169.254/latest/meta-data"),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") },
        });
        using var client = Client(inner);

        Assert.Equal("ok", await client.GetStringAsync(new Uri("https://data.example.org/moved")));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("https://data.example.org/other")));
        Assert.DoesNotContain(inner.Requests, r => r.Host == "169.254.169.254");
    }

    [Fact]
    public async Task Response_over_the_limit_is_refused_with_and_without_length()
    {
        var big = new string('x', 2048);
        using var declared = Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(big) }), limit: 1024);
        using var chunked = Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLengthStream(2048)) }), limit: 1024);

        await Assert.ThrowsAsync<HttpRequestException>(() => declared.GetStringAsync(new Uri("https://data.example.org/a")));
        await Assert.ThrowsAsync<HttpRequestException>(() => chunked.GetStringAsync(new Uri("https://data.example.org/b")));
    }

    [Fact]
    public async Task Requests_identify_ardea_in_the_user_agent()
    {
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Client(inner);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

        await client.GetAsync(new Uri("https://data.example.org/x"));

        var agent = Assert.Single(inner.UserAgents);
        Assert.StartsWith("Ardea/", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("Mozilla", agent, StringComparison.Ordinal);
    }

    [Fact]
    public void Back_off_doubles_per_failure_and_stops_at_six_hours()
    {
        Assert.InRange(Sync.SyncSourceHandler.Backoff(TimeSpan.FromMinutes(30), 1, null), TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(66));
        Assert.InRange(Sync.SyncSourceHandler.Backoff(TimeSpan.FromMinutes(30), 2, null), TimeSpan.FromMinutes(120), TimeSpan.FromMinutes(130));
        Assert.InRange(Sync.SyncSourceHandler.Backoff(TimeSpan.FromMinutes(30), 10, null), TimeSpan.FromHours(6), TimeSpan.FromHours(6) + TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Next_run_respects_a_longer_wish_of_the_source()
    {
        Assert.InRange(Sync.SyncSourceHandler.NextInterval(TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)), TimeSpan.FromHours(2), TimeSpan.FromHours(2) + TimeSpan.FromMinutes(10));
        Assert.InRange(Sync.SyncSourceHandler.NextInterval(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5)), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(33));
    }

    private static HttpClient Client(HttpMessageHandler inner, long limit = 1_000_000) =>
        new(new SourceGuardHandler(Source, limit, new HostRateLimits()) { InnerHandler = inner });

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            UserAgents.Add(request.Headers.UserAgent.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private sealed class UnknownLengthStream(int length) : MemoryStream(new byte[length])
    {
        public override bool CanSeek => false;
    }
}
