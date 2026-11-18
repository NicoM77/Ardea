using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sources.Formats.Osv;
using Ardea.Intel.Sources.Osv;

namespace Ardea.Sources.Tests.Osv;

/// <summary>Fetching from OSV.dev against recorded responses (task 1.12, master plan 5.4, 5.6).</summary>
public sealed class OsvProviderTests : IDisposable
{
    private const string Bucket = "osv-vulnerabilities.storage.googleapis.com/";

    private readonly string _work = Directory.CreateTempSubdirectory("ardea-osv-").FullName;

    [Fact]
    public async Task Incremental_run_reads_each_list_down_to_its_position_and_fetches_the_changes_oldest_first()
    {
        var http = Unchanged(new RecordedHttp())
            .On(Bucket + "npm/modified_id.csv", () => Csv(TestData.Read("osv", "npm.modified_id-head.csv"), "\"npm-2\""))
            .On(Bucket + "Go/modified_id.csv", () => Csv(TestData.Read("osv", "Go.modified_id-head.csv"), "\"go-2\""));
        var cursor = Positions(("npm", "2026-11-18T15:30:03.733768917Z"), ("Go", "2026-11-17T07:11:28.734015372Z"));

        var items = await Collect(new OsvProvider { CheckpointEvery = 1 }, Context(http, cursor));

        // npm: two advisories among malicious-package reports, which are never fetched.
        // Go: two records changed at the same instant come last, without a checkpoint between them.
        Assert.Equal(
            ["GHSA-fc5q-39xc-vjpf", "GHSA-xvcc-vqjv-phqh", "GHSA-xhv5-8fpq-g7hf", "GHSA-fq3v-74gv-27gm", "GHSA-6qr9-g2xw-cw92", "GO-2026-4527"],
            items.OfType<RawRecord>().Select(r => r.ExternalId));
        Assert.DoesNotContain(http.Requests, r => r.RequestUri!.AbsoluteUri.Contains("MAL-", StringComparison.Ordinal));
        Assert.Equal(
            ["npm:" + "2026-11-18T15:56:12.1770934+00:00", "npm:2026-11-18T19:45:04.5665224+00:00", "Go:2026-11-17T21:00:04.0216333+00:00", "Go:2026-11-18T00:15:04.3419551+00:00", "Go:2026-11-18T17:10:32.6076985+00:00"],
            Moves(cursor, items));

        var last = Cursor(items[^1]);
        Assert.Contains("npm-2", last.GetProperty("ecosystems").GetProperty("npm").GetProperty("validators").GetString(), StringComparison.Ordinal);
        Assert.Contains("go-2", last.GetProperty("ecosystems").GetProperty("Go").GetProperty("validators").GetString(), StringComparison.Ordinal);
        Assert.Equal(6, last.GetProperty("ecosystems").EnumerateObject().Count());
        var record = items.OfType<RawRecord>().First();
        Assert.Equal(new Uri("https://osv.dev/vulnerability/GHSA-fc5q-39xc-vjpf"), record.Url);
        Assert.NotNull(record.ModifiedAt);
    }

    [Fact]
    public async Task Unchanged_lists_cost_one_conditional_request_per_ecosystem()
    {
        var http = Unchanged(new RecordedHttp());

        var items = await Collect(new OsvProvider(), Context(http, Positions()));

        Assert.Empty(items);
        Assert.Equal(6, http.Requests.Count);
        Assert.All(http.Requests, r => Assert.Equal("\"unchanged\"", r.Headers.IfNoneMatch.Single().ToString()));
    }

    [Fact]
    public async Task A_list_with_only_skipped_changes_moves_the_position_without_fetching()
    {
        var malicious = string.Join('\n', TestData.Read("osv", "npm.modified_id-head.csv").Split('\n').Take(16));
        var http = Unchanged(new RecordedHttp()).On(Bucket + "npm/modified_id.csv", () => Csv(malicious, "\"npm-3\""));

        var cursor = Positions(("npm", "2026-11-18T16:30:05.599117675Z"));

        var items = await Collect(new OsvProvider(), Context(http, cursor));

        Assert.Empty(items.OfType<RawRecord>());
        Assert.Equal(["npm:2026-11-18T19:45:04.5665224+00:00"], Moves(cursor, items));
    }

    [Fact]
    public async Task First_run_reads_each_archive_within_the_backfill_window()
    {
        var http = new RecordedHttp()
            .On("/all.zip", () => Zip())
            .On(Bucket + "npm/all.zip", () => Zip([.. Archive("npm", "GHSA-rv95-896h-c2vc", "GHSA-xvcc-vqjv-phqh"), ("MAL-2026-20131.json", "not even read")]))
            .On(Bucket + "Go/all.zip", () => Zip(Archive("Go", "GHSA-pfrf-9r5f-73f5", "GO-2026-6547")));
        var context = Context(http, cursor: null, backfillFrom: Date("2026-10-01T00:00:00Z"), mode: SyncMode.Backfill);

        var items = await Collect(new OsvProvider(), context);

        // Express (changed 2026-09-10) and ZITADEL (2026-03-09) lie before the window.
        Assert.Equal(["GHSA-xvcc-vqjv-phqh", "GO-2026-6547"], items.OfType<RawRecord>().Select(r => r.ExternalId));
        Assert.Equal(6, items.OfType<Checkpoint>().Count());
        var last = Cursor(items[^1]);
        Assert.Equal("2026-10-01T00:00:00.0000000+00:00", last.GetProperty("from").GetString());
        // The position stands at the newest change in the archive, also when it lies before the window.
        Assert.Equal("2026-11-18T16:30:05.5991176+00:00", last.GetProperty("ecosystems").GetProperty("npm").GetProperty("through").GetString());
        Assert.Equal("2026-10-01T20:45:12.2275527+00:00", last.GetProperty("ecosystems").GetProperty("Go").GetProperty("through").GetString());
        Assert.Equal("2026-10-01T00:00:00.0000000+00:00", last.GetProperty("ecosystems").GetProperty("NuGet").GetProperty("through").GetString());
        Assert.Empty(Directory.GetFiles(_work));
    }

    [Fact]
    public async Task More_changes_than_the_threshold_are_read_from_the_archive_instead()
    {
        var http = Unchanged(new RecordedHttp())
            .On(Bucket + "Go/modified_id.csv", () => Csv(TestData.Read("osv", "Go.modified_id-head.csv"), "\"go-2\""))
            .On(Bucket + "Go/all.zip", () => Zip(Archive("Go", "GHSA-pfrf-9r5f-73f5", "GO-2026-6547", "GHSA-3cw6-2j68-868p")));
        var cursor = Positions(("Go", "2026-09-01T00:00:00Z"));

        var items = await Collect(new OsvProvider { ArchiveThreshold = 3 }, Context(http, cursor));

        // Twelve changes listed after the position: one archive instead of twelve requests.
        Assert.Equal(["GO-2026-6547"], items.OfType<RawRecord>().Select(r => r.ExternalId));
        Assert.DoesNotContain(http.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("GO-2026-6547.json", StringComparison.Ordinal));
        Assert.Equal(["Go:2026-10-01T20:45:12.2275527+00:00"], Moves(cursor, items));
    }

    [Fact]
    public async Task A_listed_record_that_is_gone_is_skipped()
    {
        var http = Unchanged(new RecordedHttp())
            .On("GHSA-fc5q-39xc-vjpf.json", () => new HttpResponseMessage(HttpStatusCode.NotFound))
            .On(Bucket + "npm/modified_id.csv", () => Csv(TestData.Read("osv", "npm.modified_id-head.csv"), "\"npm-2\""));

        var items = await Collect(new OsvProvider(), Context(http, Positions(("npm", "2026-11-18T15:30:03.733768917Z"))));

        Assert.Equal(["GHSA-xvcc-vqjv-phqh"], items.OfType<RawRecord>().Select(r => r.ExternalId));
    }

    [Fact]
    public async Task Single_mode_asks_the_osv_api_for_one_record_by_id()
    {
        var http = new RecordedHttp().On("api.osv.dev/v1/vulns/GHSA-rv95-896h-c2vc", () => Json(TestData.Read("osv", "npm.GHSA-rv95-896h-c2vc.json")));

        var record = Assert.IsType<RawRecord>(Assert.Single(await Collect(new OsvProvider(), Context(http, null, mode: SyncMode.Single, single: "GHSA-rv95-896h-c2vc"))));

        Assert.Equal(Date("2024-03-25T19:40:26Z"), record.PublishedAt);
        await Assert.ThrowsAsync<ArgumentException>(() => Collect(new OsvProvider(), Context(http, null, mode: SyncMode.Single, single: "CVE-2024-29041")));
    }

    [Fact]
    public async Task The_change_list_is_read_only_down_to_the_position()
    {
        var csv = TestData.Read("osv", "npm.modified_id-head.csv") + "2026-12-31T00:00:00Z,GHSA-only-if-read-on\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var list = await ChangeList.ReadAsync(stream, Date("2026-11-18T17:26:35.435103188Z"), CancellationToken.None);

        Assert.Equal(15, list.Pending.Count);
        Assert.Equal("MAL-2026-6509", list.Pending[0].Id);
        Assert.Equal(Date("2026-11-18T19:45:04.566522493Z"), list.Newest);
    }

    [Fact]
    public void Descriptor_names_operator_terms_and_hosts()
    {
        var descriptor = new OsvProvider().Descriptor;

        Assert.Equal("osv", descriptor.Key);
        Assert.Equal(OperatorKind.Recognized, descriptor.OperatorKind);
        Assert.Equal(TimeSpan.FromHours(1), descriptor.DefaultInterval);
        Assert.Contains("CC BY 4.0", descriptor.Attribution, StringComparison.Ordinal);
        Assert.Equal(["osv-vulnerabilities.storage.googleapis.com", "api.osv.dev"], descriptor.AllowedHosts);
    }

    public void Dispose() => Directory.Delete(_work, recursive: true);

    private static DateTimeOffset Date(string text) => OsvTimestamp.TryParse(text, out var date) ? date : throw new FormatException(text);

    /// <summary>A cursor with every ecosystem at the given position, the others recently and with validators.</summary>
    private static string Positions(params (string Ecosystem, string Through)[] positions)
    {
        var ecosystems = OsvEcosystems.Mvp.ToDictionary(
            e => e,
            e => positions.Any(p => p.Ecosystem == e)
                ? new { through = Date(positions.Single(p => p.Ecosystem == e).Through).ToString("O", CultureInfo.InvariantCulture), validators = (string?)"\"old\"|" }
                : new { through = "2026-11-18T20:00:00.0000000+00:00", validators = (string?)"\"unchanged\"|" });
        return JsonSerializer.Serialize(new { from = "2026-05-22T00:00:00.0000000+00:00", ecosystems });
    }

    /// <summary>Each checkpoint as ecosystem and position, where it moved one.</summary>
    private static List<string> Moves(string cursor, List<FetchItem> items)
    {
        var moves = new List<string>();
        var previous = Throughs(cursor);
        foreach (var checkpoint in items.OfType<Checkpoint>())
        {
            foreach (var (ecosystem, through) in Throughs(checkpoint.Cursor))
            {
                if (previous[ecosystem] != through)
                {
                    moves.Add($"{ecosystem}:{through}");
                }

                previous[ecosystem] = through;
            }
        }

        return moves;
    }

    private static Dictionary<string, string> Throughs(string cursor) =>
        JsonDocument.Parse(cursor).RootElement.GetProperty("ecosystems").EnumerateObject()
            .ToDictionary(e => e.Name, e => e.Value.GetProperty("through").GetString()!);

    private static JsonElement Cursor(FetchItem item) => JsonDocument.Parse(Assert.IsType<Checkpoint>(item).Cursor).RootElement;

    private static (string Name, string Content)[] Archive(string ecosystem, params string[] ids) =>
        [.. ids.Select(id => (id + ".json", TestData.Read("osv", $"{ecosystem}.{id}.json")))];

    private static RecordedHttp Unchanged(RecordedHttp http) =>
        http.On("/modified_id.csv", () => new HttpResponseMessage(HttpStatusCode.NotModified));

    private SyncContext Context(RecordedHttp http, string? cursor, DateTimeOffset? backfillFrom = null, SyncMode mode = SyncMode.Incremental, string? single = null) => new()
    {
        SourceKey = OsvProvider.Key,
        Cursor = cursor,
        Http = new HttpClient(http),
        Mode = mode,
        BackfillFrom = backfillFrom,
        SingleId = single,
        Clock = TimeProvider.System,
        Limits = new SyncLimits(1_000_000_000, TimeSpan.FromMinutes(5), int.MaxValue),
        WorkDirectory = _work,
    };

    private static async Task<List<FetchItem>> Collect(ISourceProvider provider, SyncContext context)
    {
        var items = new List<FetchItem>();
        await foreach (var item in provider.FetchAsync(context, CancellationToken.None))
        {
            items.Add(item);
        }

        return items;
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Csv(string csv, string etag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(csv, Encoding.UTF8, "text/csv") };
        response.Headers.ETag = new EntityTagHeaderValue(etag);
        return response;
    }

    /// <summary>The archive's shape in small: record files named by id at the root of the zip.</summary>
    private static HttpResponseMessage Zip(params (string Name, string Content)[] files)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) };
    }

    /// <summary>
    /// Answers recorded responses; the first matching route wins. A record URL in the bucket gets the
    /// recorded record if there is one, otherwise a minimal valid record with that id.
    /// </summary>
    private sealed class RecordedHttp : HttpMessageHandler
    {
        private readonly List<(string Match, Func<HttpResponseMessage> Respond)> _routes = [];

        public List<HttpRequestMessage> Requests { get; } = [];

        public RecordedHttp On(string match, Func<HttpResponseMessage> respond)
        {
            // Later, more specific routes go first.
            _routes.Insert(0, (match, respond));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var url = request.RequestUri!.AbsoluteUri;
            foreach (var (match, respond) in _routes)
            {
                if (url.Contains(match, StringComparison.Ordinal))
                {
                    return Task.FromResult(respond());
                }
            }

            if (url.Contains(Bucket, StringComparison.Ordinal) && url.EndsWith(".json", StringComparison.Ordinal))
            {
                var path = request.RequestUri.AbsolutePath.Trim('/').Split('/');
                var (ecosystem, id) = (path[0], path[1][..^5]);
                var recorded = TestData.Path("osv", $"{ecosystem}.{id}.json");
                return Task.FromResult(Json(File.Exists(recorded)
                    ? File.ReadAllText(recorded)
                    : $$"""{"schema_version":"1.9.0","id":"{{id}}","modified":"2026-11-18T00:00:00Z","affected":[]}"""));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
