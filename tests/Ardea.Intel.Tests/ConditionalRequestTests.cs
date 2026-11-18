using System.Net;
using System.Net.Http.Headers;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Tests;

public sealed class ConditionalRequestTests
{
    [Fact]
    public async Task Validators_are_sent_and_304_means_unchanged()
    {
        HttpRequestMessage? seen = null;
        var context = Context(request =>
        {
            seen = request;
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });

        using var result = await context.GetIfChangedAsync(new Uri("https://data.example.org/delta.json"), new HttpValidators("\"abc\"", new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero)), CancellationToken.None);

        Assert.True(result.NotModified);
        Assert.Equal("\"abc\"", seen!.Headers.IfNoneMatch.Single().ToString());
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero), seen.Headers.IfModifiedSince);
    }

    [Fact]
    public async Task Changed_response_carries_new_validators_and_the_source_wish_for_the_next_run()
    {
        var context = Context(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            response.Headers.ETag = new EntityTagHeaderValue("\"def\"");
            response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromHours(2) };
            return response;
        });

        using var result = await context.GetIfChangedAsync(new Uri("https://data.example.org/delta.json"), HttpValidators.None, CancellationToken.None);

        Assert.False(result.NotModified);
        Assert.Equal("\"def\"", result.Validators.ETag);
        Assert.Equal(TimeSpan.FromHours(2), context.NextSyncHint);
        Assert.Equal(result.Validators, HttpValidators.Parse(result.Validators.ToString()));
    }

    private static SyncContext Context(Func<HttpRequestMessage, HttpResponseMessage> respond) => new()
    {
        SourceKey = "test",
        Http = new HttpClient(new Stub(respond)),
        Mode = SyncMode.Incremental,
        Clock = TimeProvider.System,
        Limits = new SyncLimits(1_000_000, TimeSpan.FromMinutes(5), 1000),
        WorkDirectory = Path.GetTempPath(),
    };

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
