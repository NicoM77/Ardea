using System.IO.Compression;
using System.Net;
using System.Text;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sources.Signals;

namespace Ardea.Sources.Tests.Signals;

public sealed class SignalProviderTests
{
    [Fact]
    public async Task Kev_entries_become_signals_with_date_link_and_ransomware_flag()
    {
        var json = TestData.Read("cisa-kev", "known_exploited_vulnerabilities.json");
        var context = Context(_ => Ok(new StringContent(json, Encoding.UTF8, "application/json"), lastModified: new DateTimeOffset(2026, 11, 18, 19, 17, 40, TimeSpan.Zero)));

        var signals = await Collect(new CisaKevProvider().FetchSignalsAsync(context, CancellationToken.None));

        Assert.Equal(3, signals.Count);
        var log4shell = Assert.Single(signals, s => s.Value == "CVE-2021-44228");
        Assert.Equal(new DateOnly(2021, 12, 10), log4shell.KevListedOn);
        Assert.Contains(signals, s => s.KevKnownRansomware == true);
        Assert.Contains("2026-11-18T19:17:40", context.NewCursor, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unchanged_kev_catalogue_asks_with_if_modified_since_only_and_yields_nothing()
    {
        HttpRequestMessage? seen = null;
        var context = Context(request =>
        {
            seen = request;
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        }, cursor: new HttpValidators("\"etag\"", new DateTimeOffset(2026, 11, 18, 19, 17, 40, TimeSpan.Zero)).ToString());

        var signals = await Collect(new CisaKevProvider().FetchSignalsAsync(context, CancellationToken.None));

        Assert.Empty(signals);
        Assert.Empty(seen!.Headers.IfNoneMatch);
        Assert.NotNull(seen.Headers.IfModifiedSince);
    }

    [Fact]
    public async Task Epss_file_yields_score_and_percentile_per_cve()
    {
        var csv = TestData.Read("first-epss", "epss_scores-2026-11-18.csv");
        using var buffer = new MemoryStream();
        await using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(csv));
        }

        var context = Context(_ => Ok(new ByteArrayContent(buffer.ToArray())));

        var signals = await Collect(new EpssProvider().FetchSignalsAsync(context, CancellationToken.None));

        Assert.Equal(5, signals.Count);
        Assert.Equal(0.04753, Assert.Single(signals, s => s.Value == "CVE-1999-0001").EpssScore);
        Assert.InRange(Assert.Single(signals, s => s.Value == "CVE-2021-44228").EpssScore!.Value, 0.9, 1.0);
    }

    private static SyncContext Context(Func<HttpRequestMessage, HttpResponseMessage> respond, string? cursor = null) => new()
    {
        SourceKey = "test",
        Cursor = cursor,
        Http = new HttpClient(new Stub(respond)),
        Mode = SyncMode.Incremental,
        Clock = TimeProvider.System,
        Limits = new SyncLimits(100_000_000, TimeSpan.FromMinutes(5), int.MaxValue),
        WorkDirectory = Path.GetTempPath(),
    };

    private static HttpResponseMessage Ok(HttpContent content, DateTimeOffset? lastModified = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        response.Content.Headers.LastModified = lastModified;
        return response;
    }

    private static async Task<List<Signal>> Collect(IAsyncEnumerable<Signal> signals)
    {
        var list = new List<Signal>();
        await foreach (var signal in signals)
        {
            list.Add(signal);
        }

        return list;
    }

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
