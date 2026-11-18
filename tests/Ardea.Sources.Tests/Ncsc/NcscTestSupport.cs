using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Sources.Tests.Ncsc;

/// <summary>A clock that stands still unless the test moves it.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class NcscTest
{
    public static readonly DateTimeOffset Today = new(2026, 11, 18, 21, 0, 0, TimeSpan.Zero);

    public static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static SyncContext Context(string key, RecordedHttp http, string? cursor, FixedClock? clock = null, SyncMode mode = SyncMode.Incremental, string? single = null) => new()
    {
        SourceKey = key,
        Cursor = cursor,
        Http = new HttpClient(http),
        Mode = mode,
        SingleId = single,
        Clock = clock ?? new FixedClock(Today),
        Limits = new SyncLimits(50_000_000, TimeSpan.FromMinutes(5), int.MaxValue),
        WorkDirectory = Path.GetTempPath(),
    };

    public static async Task<List<FetchItem>> Collect(ISourceProvider provider, SyncContext context)
    {
        var items = new List<FetchItem>();
        await foreach (var item in provider.FetchAsync(context, CancellationToken.None))
        {
            items.Add(item);
        }

        return items;
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, new MediaTypeHeaderValue(mediaType)) };

    public static string LastCursor(this List<FetchItem> items) => items.OfType<Checkpoint>().Last().Cursor;
}

/// <summary>Answers recorded responses by URL fragment and remembers every request; an unknown URL is a 404.</summary>
internal sealed class RecordedHttp : HttpMessageHandler
{
    private readonly List<(string Match, Func<HttpResponseMessage> Respond)> _routes = [];

    public List<Uri> Requests { get; } = [];

    public IEnumerable<string> Paths => Requests.Select(r => r.PathAndQuery);

    public RecordedHttp On(string match, Func<HttpResponseMessage> respond)
    {
        _routes.Add((match, respond));
        return this;
    }

    public RecordedHttp On(string match, string json) => On(match, () => NcscTest.Json(json));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        var url = request.RequestUri!.AbsoluteUri;
        foreach (var (match, respond) in _routes)
        {
            if (url.Contains(match, StringComparison.Ordinal))
            {
                return Task.FromResult(respond());
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
