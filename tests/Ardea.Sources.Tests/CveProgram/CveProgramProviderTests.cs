using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sources.CveProgram;

namespace Ardea.Sources.Tests.CveProgram;

/// <summary>Fetching from the CVE Program against recorded responses (task 1.4, master plan 5.5).</summary>
public sealed class CveProgramProviderTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("ardea-cve-").FullName;

    [Fact]
    public async Task Incremental_run_fetches_each_change_after_the_cursor_once_and_checkpoints_per_log_entry()
    {
        var log = TestData.Read("cve-program", "deltaLog-trimmed.json");
        var http = new RecordedHttp().On("deltaLog.json", () => Json(log, etag: "\"log-1\""));
        // The cursor stands at the oldest entry: only the two newer ones are due.
        var context = Context(http, new CveCursor(DateTimeOffset.Parse("2026-11-18T19:58:28.347Z", System.Globalization.CultureInfo.InvariantCulture), null).ToString());

        var items = await Collect(new CveProgramProvider(), context);

        var records = items.OfType<RawRecord>().Select(r => r.ExternalId).ToList();
        Assert.Equal(["CVE-2026-132136", "CVE-2026-63129", "CVE-2026-114938", "CVE-2026-120049", "CVE-2026-132137", "CVE-2026-73967", "CVE-2026-123007", "CVE-2026-123633"], records);
        var checkpoints = items.OfType<Checkpoint>().Select(c => JsonDocument.Parse(c.Cursor).RootElement).ToList();
        Assert.Equal(2, checkpoints.Count);
        Assert.Equal("2026-11-18T20:17:33.4720000+00:00", checkpoints[^1].GetProperty("through").GetString());
        Assert.Contains("log-1", checkpoints[^1].GetProperty("validators").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_record_listed_in_several_entries_is_fetched_once()
    {
        var entries = JsonSerializer.Deserialize<List<DeltaEntry>>(TestData.Read("cve-program", "deltaLog-trimmed.json"))!;
        var duplicate = entries[0].Updated![0];
        entries[1].Updated!.Add(duplicate);
        entries[1].New!.Add(duplicate);

        // The log is newest first; the cursor stands at the oldest entry.
        var plan = DeltaPlan.Create(entries, entries[2].FetchTime);

        Assert.Equal(1, plan.Steps.SelectMany(s => s.Changes).Count(c => c.CveId == duplicate.CveId));
        Assert.Equal(plan.Steps.Select(s => s.FetchTime).Order(), plan.Steps.Select(s => s.FetchTime));
    }

    [Fact]
    public void Cursor_older_than_the_log_is_a_gap_that_only_the_archive_closes()
    {
        var entries = JsonSerializer.Deserialize<List<DeltaEntry>>(TestData.Read("cve-program", "deltaLog-trimmed.json"))!;

        Assert.True(DeltaPlan.Create(entries, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)).GapTooLong);
        Assert.Empty(DeltaPlan.Create(entries, entries[0].FetchTime).Steps);
    }

    [Fact]
    public async Task Unchanged_log_costs_one_conditional_request()
    {
        var http = new RecordedHttp().On("deltaLog.json", () => new HttpResponseMessage(HttpStatusCode.NotModified));
        var context = Context(http, new CveCursor(DateTimeOffset.UtcNow, "\"log-1\"|").ToString());

        var items = await Collect(new CveProgramProvider(), context);

        Assert.Empty(items);
        Assert.Equal("\"log-1\"", Assert.Single(http.Requests).Headers.IfNoneMatch.Single().ToString());
    }

    [Fact]
    public async Task First_run_reads_the_full_archive_within_the_backfill_window()
    {
        var archive = ZipOfZip(
            ("cves/2026/119xxx/CVE-2026-119725.json", TestData.Read("cve-program", "CVE-2026-119725.json")),
            ("cves/2021/44xxx/CVE-2021-44228.json", TestData.Read("cve-program", "CVE-2021-44228.json")),
            ("cves/2022/22xxx/CVE-2022-22965.json", TestData.Read("cve-program", "CVE-2022-22965.json")),
            ("cves/delta.json", "{}"));
        var releases = """[{"assets":[{"name":"2026-11-18_all_CVEs_at_midnight.zip.zip","browser_download_url":"https://github.com/CVEProject/cvelistV5/releases/download/x/2026-11-18_all_CVEs_at_midnight.zip.zip"}]}]""";
        var http = new RecordedHttp()
            .On("api.github.com", () => Json(releases))
            .On("all_CVEs_at_midnight", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var context = Context(http, cursor: null, backfillFrom: new DateTimeOffset(2025, 11, 1, 0, 0, 0, TimeSpan.Zero), mode: SyncMode.Backfill);

        var items = await Collect(new CveProgramProvider(), context);

        // Log4Shell was last updated in 2025-10, Spring4Shell before; only the 2026 record and the
        // records updated inside the window come through.
        var ids = items.OfType<RawRecord>().Select(r => r.ExternalId).ToList();
        Assert.Contains("CVE-2026-119725", ids);
        Assert.DoesNotContain("CVE-2022-22965", ids);
        var cursor = JsonDocument.Parse(Assert.IsType<Checkpoint>(items[^1]).Cursor).RootElement;
        Assert.Equal("2026-11-18T00:00:00.0000000+00:00", cursor.GetProperty("through").GetString());
    }

    [Fact]
    public async Task Pause_longer_than_the_log_falls_back_to_the_archive()
    {
        var archive = ZipOfZip(("cves/2026/119xxx/CVE-2026-119725.json", TestData.Read("cve-program", "CVE-2026-119725.json")));
        var releases = """[{"assets":[{"name":"2026-11-18_all_CVEs_at_midnight.zip.zip","browser_download_url":"https://github.com/CVEProject/cvelistV5/releases/download/x/2026-11-18_all_CVEs_at_midnight.zip.zip"}]}]""";
        var http = new RecordedHttp()
            .On("deltaLog.json", () => Json(TestData.Read("cve-program", "deltaLog-trimmed.json")))
            .On("api.github.com", () => Json(releases))
            .On("all_CVEs_at_midnight", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var context = Context(http, new CveCursor(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), null).ToString(), backfillFrom: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        var items = await Collect(new CveProgramProvider(), context);

        Assert.Equal(["CVE-2026-119725"], items.OfType<RawRecord>().Select(r => r.ExternalId));
    }

    [Fact]
    public async Task Single_mode_asks_the_cve_services_for_one_record()
    {
        var http = new RecordedHttp().On("cveawg.mitre.org/api/cve/CVE-2021-44228", () => Json(TestData.Read("cve-program", "CVE-2021-44228.json")));
        var context = Context(http, cursor: null, mode: SyncMode.Single, single: "CVE-2021-44228");

        var record = Assert.IsType<RawRecord>(Assert.Single(await Collect(new CveProgramProvider(), context)));

        Assert.Equal("CVE-2021-44228", record.ExternalId);
        Assert.Equal(new DateTimeOffset(2021, 12, 10, 0, 0, 0, TimeSpan.Zero), record.PublishedAt);
    }

    public void Dispose() => Directory.Delete(_work, recursive: true);

    private SyncContext Context(RecordedHttp http, string? cursor, DateTimeOffset? backfillFrom = null, SyncMode mode = SyncMode.Incremental, string? single = null) => new()
    {
        SourceKey = CveProgramProvider.Key,
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

    private static HttpResponseMessage Json(string json, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (etag is not null)
        {
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        }

        return response;
    }

    /// <summary>The archive's shape in small: an outer zip around a zip of record files.</summary>
    private static byte[] ZipOfZip(params (string Path, string Content)[] files)
    {
        using var innerStream = new MemoryStream();
        using (var inner = new ZipArchive(innerStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(inner.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        using var outerStream = new MemoryStream();
        using (var outer = new ZipArchive(outerStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = outer.CreateEntry("cves.zip").Open();
            entry.Write(innerStream.ToArray());
        }

        return outerStream.ToArray();
    }

    /// <summary>Answers recorded responses; any record URL on GitHub gets the recorded record if there is one.</summary>
    private sealed class RecordedHttp : HttpMessageHandler
    {
        private readonly List<(string Match, Func<HttpResponseMessage> Respond)> _routes = [];

        public List<HttpRequestMessage> Requests { get; } = [];

        public RecordedHttp On(string match, Func<HttpResponseMessage> respond)
        {
            _routes.Add((match, respond));
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

            if (url.EndsWith(".json", StringComparison.Ordinal) && url.Contains("/cves/", StringComparison.Ordinal))
            {
                var id = url[(url.LastIndexOf('/') + 1)..^5];
                var recorded = TestData.Path("cve-program", id + ".json");
                return Task.FromResult(File.Exists(recorded)
                    ? Json(File.ReadAllText(recorded))
                    : Json("{\"dataVersion\":\"5.2\",\"cveMetadata\":{\"cveId\":\"" + id + "\",\"state\":\"PUBLISHED\",\"dateUpdated\":\"2026-11-18T20:00:00Z\"}}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
