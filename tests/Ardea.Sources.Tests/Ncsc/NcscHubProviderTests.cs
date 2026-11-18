using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sources.Ncsc;

namespace Ardea.Sources.Tests.Ncsc;

/// <summary>The Cyber Security Hub reader against recorded responses (task 1.9, master plan 5.5).</summary>
public sealed class NcscHubProviderTests
{
    private static readonly long[] Recorded = [13196, 13203, 13198, 13083, 13054, 13163, 13180];

    public static TheoryData<string> Posts => [.. TestData.Files("ncsc-hub", "post-*.json")];

    private static NcscHubProvider Provider => new();

    [Fact]
    public void Descriptor_is_conservative_and_names_only_the_hub_host()
    {
        var descriptor = Provider.Descriptor;

        Assert.Equal("ncsc-hub", descriptor.Key);
        Assert.Equal(OperatorKind.Authority, descriptor.OperatorKind);
        Assert.Equal(TimeSpan.FromMinutes(30), descriptor.DefaultInterval);
        Assert.Equal(TimeSpan.FromMinutes(30), descriptor.MinimumInterval);
        Assert.Equal(["security-hub.ncsc.admin.ch"], descriptor.AllowedHosts);
        Assert.Equal(TermsMode.Full, descriptor.Terms);
    }

    [Fact]
    public async Task First_run_reads_one_list_page_and_every_post_oldest_change_first()
    {
        var http = Hub(Recorded);

        var items = await NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", http, cursor: null));

        Assert.Equal(
            [
                "/api/v1/posts/dashboard?pageSize=50&pageIndex=0",
                .. new long[] { 13083, 13180, 13163, 13054, 13196, 13198, 13203 }.Select(id => $"/api/v1/posts/{id}/details"),
            ],
            http.Paths);
        Assert.All(http.Requests, r => Assert.Equal("security-hub.ncsc.admin.ch", r.Host));
        var record = items.OfType<RawRecord>().First(r => r.ExternalId == "13198");
        Assert.Equal("https://security-hub.ncsc.admin.ch/#/posts/13198", record.Url!.AbsoluteUri);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 21, 57, 54, 549, TimeSpan.Zero).AddTicks(4550), record.ModifiedAt);

        var cursor = HubCursor.Parse(Assert.IsType<Checkpoint>(items[^1]).Cursor)!;
        Assert.Equal(NcscTest.Today, cursor.Sweep);
        Assert.Equal(Recorded.Order(), cursor.Posts.Keys.Order());
        Assert.Equal("2026-11-17T21:57:54.549455Z", cursor.Posts[13198]);
    }

    [Fact]
    public async Task Later_run_fetches_only_the_post_whose_last_change_moved()
    {
        var first = await NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", Hub(Recorded), cursor: null));
        var cursor = first.LastCursor();

        var http = Hub(Recorded, moved: new() { [13196] = "2026-11-18T21:00:00.000000Z" });
        var clock = new FixedClock(NcscTest.Today.AddMinutes(30));
        var items = await NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", http, cursor, clock));

        Assert.Equal(["/api/v1/posts/dashboard?pageSize=50&pageIndex=0", "/api/v1/posts/13196/details"], http.Paths);
        Assert.Equal(["13196"], items.OfType<RawRecord>().Select(r => r.ExternalId));
        var next = HubCursor.Parse(items.LastCursor())!;
        Assert.Equal("2026-11-18T21:00:00.000000Z", next.Posts[13196]);
        Assert.Equal(NcscTest.Today, next.Sweep);
    }

    [Fact]
    public async Task Unchanged_list_costs_one_request_and_no_checkpoint()
    {
        var cursor = (await NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", Hub(Recorded), cursor: null))).LastCursor();
        var http = Hub(Recorded);

        var items = await NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", http, cursor, new FixedClock(NcscTest.Today.AddHours(1))));

        Assert.Empty(items);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Daily_sweep_reads_all_pages_and_fetches_only_what_is_new_or_changed()
    {
        var items0 = Items(PageOf("dashboard-page0.json"));
        var items1 = Items(PageOf("dashboard-page1.json"));
        var known = items0.Concat(items1).Where(i => i["id"]!.GetValue<long>() != 13198)
            .ToDictionary(i => i["id"]!.GetValue<long>(), i => i["lastChange"]!.GetValue<string>());
        var stale = new HubCursor(NcscTest.Today.AddHours(-25), known).ToString();

        // Post 13198 slipped past the first page (order is unreliable) and is only seen on page 1 of the sweep.
        var http = new RecordedHttp()
            .On("pageIndex=0", TestData.Read("ncsc-hub", "dashboard-page0.json"))
            .On("pageIndex=1", TestData.Read("ncsc-hub", "dashboard-page1.json"))
            .On("pageIndex=", EmptyPage(336))
            .On("/posts/13198/details", TestData.Read("ncsc-hub", "post-13198.json"));
        var items = await NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", http, stale));

        Assert.Equal(7, http.Paths.Count(p => p.Contains("dashboard", StringComparison.Ordinal)));
        Assert.Equal(["/api/v1/posts/13198/details"], http.Paths.Where(p => p.Contains("details", StringComparison.Ordinal)));
        Assert.Equal(["13198"], items.OfType<RawRecord>().Select(r => r.ExternalId));
        Assert.Equal(NcscTest.Today, HubCursor.Parse(items.LastCursor())!.Sweep);
    }

    [Fact]
    public async Task Sweep_not_yet_due_reads_the_first_page_only()
    {
        var known = Items(PageOf("dashboard-page0.json")).ToDictionary(i => i["id"]!.GetValue<long>(), i => i["lastChange"]!.GetValue<string>());
        var recent = new HubCursor(NcscTest.Today.AddHours(-23), known).ToString();
        var http = new RecordedHttp().On("pageIndex=0", TestData.Read("ncsc-hub", "dashboard-page0.json"));

        var items = await NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", http, recent));

        Assert.Empty(items);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Backfill_mode_sweeps_even_when_the_last_sweep_is_recent()
    {
        var recent = new HubCursor(NcscTest.Today.AddMinutes(-5), []).ToString();
        // 120 posts announced at 50 per page: three list requests.
        var http = new RecordedHttp().On("pageIndex=0", PageWith(120, [(13198, "2026-11-17T21:57:54.549455Z")])).On("pageIndex=", EmptyPage(120));

        await NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", http, recent, mode: SyncMode.Backfill));

        Assert.Equal(3, http.Paths.Count(p => p.Contains("dashboard", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_cve_id_added_later_turns_the_same_post_into_evidence_for_that_cve()
    {
        var real = JsonNode.Parse(TestData.Read("ncsc-hub", "post-13198.json"))!;
        var before = real.DeepClone();
        before["title"] = "[Advisory] Java4Shell: Critical Remote Code Execution, Patch Now";
        before["summary"] = real["summary"]!.GetValue<string>().Replace(" (CVE-2026-114938)", string.Empty, StringComparison.Ordinal);
        before["content"] = System.Text.RegularExpressions.Regex.Replace(real["content"]!.GetValue<string>(), "CVE-2026-114938", "the-vulnerability", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        before["lastModified"] = "2026-11-17T11:59:00.000000Z";
        before["history"] = new JsonArray(real["history"]![0]!.DeepClone());
        Assert.Empty(CveText(before));

        var http1 = new RecordedHttp()
            .On("pageIndex=0", PageWith(1, [(13198, "2026-11-17T11:59:00.000000Z")]))
            .On("/posts/13198/details", before.ToJsonString());
        var run1 = await NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", http1, cursor: null));
        var early = Provider.Normalize(run1.OfType<RawRecord>().Single()).Observation!;

        var http2 = new RecordedHttp()
            .On("pageIndex=0", PageWith(1, [(13198, "2026-11-17T21:57:54.549455Z")]))
            .On("/posts/13198/details", TestData.Read("ncsc-hub", "post-13198.json"));
        var run2 = await NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", http2, run1.LastCursor(), new FixedClock(NcscTest.Today.AddMinutes(30))));
        var late = Provider.Normalize(run2.OfType<RawRecord>().Single()).Observation!;

        Assert.Equal(ObservationKind.Advisory, early.Kind);
        Assert.Empty(early.Claims);
        var claim = Assert.Single(late.Claims);
        Assert.Equal([new Identifier("CVE", "CVE-2026-114938")], claim.Aliases);
        Assert.Equal([new Identifier("NCSC-HUB", "13198")], claim.Related);
    }

    [Fact]
    public async Task Cursor_stays_bounded_and_checkpoints_come_in_batches()
    {
        var ids = Enumerable.Range(1, 30).Select(i => (long)i).ToArray();
        // Synthetic posts: only the batching matters here.
        var http = new RecordedHttp().On("pageIndex=0", PageWith(30, [.. ids.Select(i => (i, $"2026-11-01T00:00:{i:00}.000000Z"))]));
        foreach (var id in ids)
        {
            http.On($"/posts/{id}/details", $$"""{"id":{{id}},"tlpStatus":"Clear","title":"synthetic {{id}}","content":""}""");
        }

        var items = await NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", http, cursor: null));

        Assert.Equal(30, items.OfType<RawRecord>().Count());
        var positions = items.Select((item, index) => (item, index)).Where(x => x.item is Checkpoint).Select(x => x.index).ToList();
        Assert.Equal([25, 31], positions);
        Assert.Equal(25, HubCursor.Parse(items.OfType<Checkpoint>().First().Cursor)!.Posts.Count);

        var big = new HubCursor(NcscTest.Today, Enumerable.Range(1, HubCursor.MaxPosts + 50).ToDictionary(i => (long)i, _ => "x")).ToString();
        var parsed = HubCursor.Parse(big)!;
        Assert.Equal(HubCursor.MaxPosts, parsed.Posts.Count);
        Assert.False(parsed.Posts.ContainsKey(50));
        Assert.True(parsed.Posts.ContainsKey(HubCursor.MaxPosts + 50));
    }

    [Fact]
    public async Task A_post_that_vanished_between_list_and_details_is_skipped()
    {
        var http = new RecordedHttp().On("pageIndex=0", PageWith(1, [(13198, "2026-11-17T21:57:54.549455Z")]));

        var items = await NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", http, cursor: null));

        Assert.Empty(items.OfType<RawRecord>());
    }

    [Fact]
    public async Task Single_mode_fetches_one_post_by_number()
    {
        var http = new RecordedHttp().On("/posts/13198/details", TestData.Read("ncsc-hub", "post-13198.json"));

        var record = Assert.IsType<RawRecord>(Assert.Single(await NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", http, null, mode: SyncMode.Single, single: "13198"))));

        Assert.Equal("13198", record.ExternalId);
    }

    [Fact]
    public async Task An_empty_list_that_announces_posts_is_suspicious()
    {
        var http = new RecordedHttp().On("pageIndex=0", EmptyPage(336));

        await Assert.ThrowsAsync<InvalidOperationException>(() => NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", http, cursor: null)));
    }

    [Fact]
    public async Task A_maintenance_page_instead_of_json_is_suspicious()
    {
        var http = new RecordedHttp().On("pageIndex=0", () => NcscTest.Json("<html>Maintenance</html>", mediaType: "text/html"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", http, cursor: null)));
    }

    [Fact]
    public async Task A_detail_answering_for_another_post_is_suspicious()
    {
        var http = new RecordedHttp()
            .On("pageIndex=0", PageWith(1, [(13196, "2026-11-17T10:08:28.181507Z")]))
            .On("/posts/13196/details", TestData.Read("ncsc-hub", "post-13198.json"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => NcscTest.Collect(Provider, NcscTest.Context("ncsc-hub", http, cursor: null)));
    }

    [Fact]
    public async Task A_request_to_wait_is_passed_on_to_the_core()
    {
        var http = new RecordedHttp().On("pageIndex=0", () =>
        {
            var response = NcscTest.Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return response;
        });
        var context = NcscTest.Context("ncsc-hub", http, cursor: null);

        await Assert.ThrowsAsync<HttpRequestException>(() => NcscTest.Collect(Provider, context));

        Assert.Equal(TimeSpan.FromMinutes(10), context.NextSyncHint);
    }

    [Fact]
    public void A_post_that_is_not_tlp_clear_goes_to_quarantine()
    {
        var amber = TestData.Read("ncsc-hub", "post-13196.json").Replace("\"Clear\"", "\"Amber\"", StringComparison.Ordinal);

        var result = Provider.Normalize(new RawRecord("13196", amber, "application/json"));

        Assert.Null(result.Observation);
        Assert.Contains("Amber", result.QuarantineReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Cve_ids_are_found_in_title_summary_and_text_with_any_sequence_length()
    {
        var observation = Normalize("post-13203.json");

        // Neither id is in the title or summary of post 13203; both come from the text, with six-digit sequence numbers.
        Assert.Equal(["CVE-2026-123007", "CVE-2026-123633"], observation.Claims.Select(c => c.Aliases.Single().Value).Order());
        Assert.DoesNotContain("CVE-", observation.Title + observation.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Posts))]
    public void Recorded_post_reads_into_the_reviewed_observation(string file)
    {
        var observation = Normalize(file);

        Assert.Equal(ObservationKind.Advisory, observation.Kind);
        Assert.Equal("en", observation.Language);
        Assert.DoesNotContain("<", observation.Summary, StringComparison.Ordinal);
        var shortened = observation with { Description = observation.Description is { } d ? d[..Math.Min(160, d.Length)] + $" … ({d.Length} characters)" : null };
        Snapshot.Match(JsonSerializer.Serialize(shortened, NcscTest.SnapshotJson), "ncsc-hub." + Path.GetFileNameWithoutExtension(file));
    }

    private static Observation Normalize(string file)
    {
        var result = Provider.Normalize(new RawRecord(file, TestData.Read("ncsc-hub", file), "application/json"));
        Assert.Null(result.QuarantineReason);
        return result.Observation!;
    }

    private static IEnumerable<string> CveText(JsonNode post) => System.Text.RegularExpressions.Regex.Matches(post.ToJsonString(), @"(?i)CVE-\d{4}-\d+").Select(m => m.Value);

    private static JsonNode PageOf(string file) => JsonNode.Parse(TestData.Read("ncsc-hub", file))!;

    private static List<JsonNode> Items(JsonNode page) => [.. page["items"]!.AsArray().Select(i => i!.DeepClone())];

    private static string EmptyPage(int count) => $$"""{"pageIndex":9,"pageSize":50,"items":[],"count":{{count}}}""";

    /// <summary>A list page of the recorded items with the given ids, in the real shape.</summary>
    private static string PageWith(int count, IEnumerable<(long Id, string LastChange)> ids)
    {
        var recorded = Items(PageOf("dashboard-page0.json")).Concat(Items(PageOf("dashboard-page1.json"))).ToDictionary(i => i["id"]!.GetValue<long>());
        var items = new JsonArray();
        foreach (var (id, lastChange) in ids)
        {
            var item = recorded.TryGetValue(id, out var real)
                ? real.DeepClone()
                : JsonNode.Parse($$"""{"id":{{id}},"created":"{{lastChange}}","publicationStatus":"Open","summary":"synthetic","title":"synthetic {{id}}","tlpStatus":"Clear"}""")!;
            item["lastChange"] = lastChange;
            items.Add(item);
        }

        return new JsonObject { ["pageIndex"] = 0, ["pageSize"] = 50, ["items"] = items, ["count"] = count }.ToJsonString();
    }


    /// <summary>The hub as recorded for the given posts; <paramref name="moved"/> gives some posts a later <c>lastChange</c>.</summary>
    private static RecordedHttp Hub(long[] ids, Dictionary<long, string>? moved = null)
    {
        var recorded = Items(PageOf("dashboard-page0.json")).Concat(Items(PageOf("dashboard-page1.json"))).ToDictionary(i => i["id"]!.GetValue<long>());
        var http = new RecordedHttp().On("pageIndex=0", PageWith(ids.Length, ids.Select(id => (id, moved?.GetValueOrDefault(id) ?? recorded[id]["lastChange"]!.GetValue<string>()))));
        foreach (var id in ids)
        {
            http.On($"/posts/{id}/details", TestData.Read("ncsc-hub", $"post-{id}.json"));
        }

        return http;
    }
}
