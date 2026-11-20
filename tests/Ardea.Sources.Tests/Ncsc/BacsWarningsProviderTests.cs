using System.Text.Json;
using System.Text.Json.Nodes;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sources.Ncsc;

namespace Ardea.Sources.Tests.Ncsc;

public sealed class BacsWarningsProviderTests
{
    private static readonly string[] Slugs = ["warnung-java-2-en", "warnung-java-en", "warnung-php-en", "warnung-estv-en"];

    private const string OverviewUpdatedAt = "2026-11-18T08:32:21.046Z";

    public static TheoryData<string> Pages => [.. Slugs.Select(s => s + ".json")];

    private static BacsWarningsProvider Provider => new();

    [Fact]
    public void Descriptor_keeps_metadata_only_and_names_only_the_website_host()
    {
        var descriptor = Provider.Descriptor;

        Assert.Equal("bacs-warnings", descriptor.Key);
        Assert.Equal(TermsMode.MetadataOnly, descriptor.Terms);
        Assert.Equal(OperatorKind.Authority, descriptor.OperatorKind);
        Assert.Equal(TimeSpan.FromMinutes(30), descriptor.DefaultInterval);
        Assert.Equal(TimeSpan.FromMinutes(30), descriptor.MinimumInterval);
        Assert.Equal(["www.bacs.admin.ch"], descriptor.AllowedHosts);
    }

    [Fact]
    public async Task First_run_reads_the_overview_and_every_listed_page_and_keeps_metadata_only()
    {
        var http = Website();

        var items = await NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", http, cursor: null));

        Assert.Equal("/api/collector/en/current-incidents.json", http.Paths.First());
        Assert.Equal(Slugs.Select(s => "/api/collector/en/" + s + ".json").Order(), http.Paths.Skip(1).Order());
        Assert.All(http.Requests, r => Assert.Equal("www.bacs.admin.ch", r.Host));

        var records = items.OfType<RawRecord>().ToList();
        Assert.Equal(4, records.Count);
        var java = records.Single(r => r.Url!.AbsoluteUri.EndsWith("warnung-java-2-en", StringComparison.Ordinal));
        Assert.Equal("UqlMAT0gJSWG", java.ExternalId);
        Assert.Equal("https://www.bacs.admin.ch/en/warnung-java-2-en", java.Url!.AbsoluteUri);
        Assert.Equal(DateTimeOffset.Parse("2026-11-18T06:33:15.645Z", System.Globalization.CultureInfo.InvariantCulture), java.ModifiedAt);

        using var stored = JsonDocument.Parse(java.Content);
        Assert.Equal(
            ["documentId", "groupId", "hubPosts", "lang", "publishedAt", "summary", "title", "updatedAt", "url"],
            stored.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.True(java.Content.Length < 1200, "The raw record should be small.");
        Assert.DoesNotContain("Further information", java.Content, StringComparison.Ordinal);
        Assert.Equal([13198], stored.RootElement.GetProperty("hubPosts").EnumerateArray().Select(e => e.GetInt64()));
    }

    [Fact]
    public async Task Unchanged_overview_costs_one_request()
    {
        var cursor = (await NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", Website(), cursor: null))).LastCursor();
        var http = Website();

        var items = await NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", http, cursor, new FixedClock(NcscTest.Today.AddMinutes(30))));

        Assert.Empty(items);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Page_edited_in_place_is_found_when_the_overview_moved_and_only_that_page_is_delivered()
    {
        var cursor = (await NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", Website(), cursor: null))).LastCursor();
        var http = Website(overviewUpdatedAt: "2026-11-18T22:00:00.000Z", pageUpdatedAt: new() { ["warnung-php-en"] = "2026-11-18T21:59:00.000Z" });

        var items = await NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", http, cursor, new FixedClock(NcscTest.Today.AddMinutes(30))));

        Assert.Equal(5, http.Requests.Count);
        var record = Assert.Single(items.OfType<RawRecord>());
        Assert.Contains("PHP", record.Content, StringComparison.Ordinal);
        Assert.Equal(DateTimeOffset.Parse("2026-11-18T21:59:00.000Z", System.Globalization.CultureInfo.InvariantCulture), record.ModifiedAt);
        Assert.Contains("21:59", items.LastCursor(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changed_teaser_fetches_just_that_page()
    {
        var cursor = (await NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", Website(), cursor: null))).LastCursor();
        var http = Website(teaserDescription: new() { ["/en/warnung-estv-en"] = "Updated lead of the phishing warning." });

        var items = await NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", http, cursor, new FixedClock(NcscTest.Today.AddMinutes(30))));

        Assert.Equal(["/api/collector/en/current-incidents.json", "/api/collector/en/warnung-estv-en.json"], http.Paths);
        Assert.Empty(items.OfType<RawRecord>());
        Assert.Single(items.OfType<Checkpoint>());
    }

    [Fact]
    public async Task Daily_check_finds_an_edit_that_left_the_overview_untouched()
    {
        var cursor = (await NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", Website(), cursor: null))).LastCursor();
        var http = Website(pageUpdatedAt: new() { ["warnung-java-en"] = "2026-11-19T08:00:00.000Z" });

        var soon = await NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", http, cursor, new FixedClock(NcscTest.Today.AddHours(2))));
        var nextDay = await NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", Website(pageUpdatedAt: new() { ["warnung-java-en"] = "2026-11-19T08:00:00.000Z" }), cursor, new FixedClock(NcscTest.Today.AddHours(25))));

        Assert.Empty(soon);
        Assert.Equal(["warnung-java-en"], nextDay.OfType<RawRecord>().Select(r => JsonDocument.Parse(r.Content).RootElement.GetProperty("url").GetString()!.Split('/')[^1]));
    }

    [Fact]
    public async Task Pages_that_left_the_overview_drop_out_of_the_cursor()
    {
        var cursor = (await NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", Website(), cursor: null))).LastCursor();
        var http = Website(only: ["warnung-java-2-en", "warnung-java-en"]);

        var items = await NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", http, cursor, new FixedClock(NcscTest.Today.AddMinutes(30))));

        Assert.Equal(2, BacsCursor.Parse(items.LastCursor())!.Docs.Count);
    }

    [Fact]
    public async Task An_overview_without_warnings_is_suspicious()
    {
        var http = new RecordedHttp().On("current-incidents", """{"documentId":1,"content":[]}""");

        await Assert.ThrowsAsync<InvalidOperationException>(() => NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", http, cursor: null)));
    }

    [Fact]
    public async Task A_page_without_document_id_is_suspicious()
    {
        var broken = new RecordedHttp().On("current-incidents", Overview(["warnung-estv-en"], null, null)).On("warnung-estv-en", """{"updatedAt":"2026-11-18T00:00:00Z","metadata":{}}""");

        await Assert.ThrowsAsync<InvalidOperationException>(() => NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", broken, cursor: null)));
    }

    [Fact]
    public async Task Single_mode_fetches_one_page_by_path()
    {
        var http = Website();

        var record = Assert.IsType<RawRecord>(Assert.Single(await NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", http, null, mode: SyncMode.Single, single: "en/warnung-java-2-en"))));

        Assert.Equal("UqlMAT0gJSWG", record.ExternalId);
    }

    [Fact]
    public async Task Warning_with_a_cve_in_the_lead_is_an_advisory_and_keeps_the_hub_post_as_related_identifier()
    {
        var observation = await NormalizedAsync("warnung-java-2-en");

        Assert.Equal(ObservationKind.Advisory, observation.Kind);
        var claim = Assert.Single(observation.Claims);
        Assert.Equal([new Identifier("CVE", "CVE-2026-114938")], claim.Aliases);
        Assert.Equal([new Identifier("NCSC-HUB", "13198")], claim.Related);
    }

    [Fact]
    public async Task Warning_without_a_cve_stays_a_warning_without_claims_and_links_the_hub_post()
    {
        var observation = await NormalizedAsync("warnung-java-en");

        Assert.Equal(ObservationKind.Warning, observation.Kind);
        Assert.Empty(observation.Claims);
        Assert.Contains(observation.References, r => r.Url.AbsoluteUri == "https://security-hub.ncsc.admin.ch/#/posts/13198" && r.Tags.Contains("related"));
    }

    [Fact]
    public async Task Phishing_warning_has_no_claims_and_no_hub_link()
    {
        var observation = await NormalizedAsync("warnung-estv-en");

        Assert.Equal(ObservationKind.Warning, observation.Kind);
        Assert.Single(observation.References);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Recorded_page_reads_into_the_reviewed_metadata_only_observation(string file)
    {
        var observation = await NormalizedAsync(Path.GetFileNameWithoutExtension(file));

        Assert.Null(observation.Description);
        Assert.Equal("en", observation.Language);
        Assert.DoesNotContain("<", observation.Summary, StringComparison.Ordinal);
        Snapshot.Match(JsonSerializer.Serialize(observation, NcscTest.SnapshotJson), "bacs-warnings." + Path.GetFileNameWithoutExtension(file));
    }

    [Fact]
    public void Stored_record_that_is_not_json_or_has_no_title_goes_to_quarantine()
    {
        Assert.NotNull(Provider.Normalize(new RawRecord("x", "<html/>", "application/json")).QuarantineReason);
        Assert.NotNull(Provider.Normalize(new RawRecord("x", """{"url":"https://www.bacs.admin.ch/en/x"}""", "application/json")).QuarantineReason);
    }

    private static async Task<Observation> NormalizedAsync(string slug)
    {
        var http = Website(only: [slug]);
        var record = Assert.IsType<RawRecord>(Assert.Single((await NcscTest.Collect(Provider, NcscTest.Context("bacs-warnings", http, cursor: null))).OfType<RawRecord>()));
        var result = Provider.Normalize(record);
        Assert.Null(result.QuarantineReason);
        return result.Observation!;
    }

    private static string Overview(string[] slugs, string? updatedAt, Dictionary<string, string>? teaserDescription)
    {
        var node = JsonNode.Parse(TestData.Read("bacs-warnings", "current-incidents.json"))!;
        var keep = slugs.Select(s => "/en/" + s).ToHashSet();
        Trim(node, keep, teaserDescription);
        node["updatedAt"] = updatedAt ?? OverviewUpdatedAt;
        return node.ToJsonString();
    }

    private static void Trim(JsonNode? node, HashSet<string> keep, Dictionary<string, string>? description)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var child in array.ToList())
                {
                    if (child is JsonObject o && (string?)o["identifier"] == "teaser-item" && !keep.Contains((string)o["properties"]!["slug"]!))
                    {
                        array.Remove(child);
                    }
                    else
                    {
                        Trim(child, keep, description);
                    }
                }

                break;
            case JsonObject obj:
                if ((string?)obj["identifier"] == "teaser-item" && description is not null && description.TryGetValue((string)obj["properties"]!["slug"]!, out var text))
                {
                    obj["properties"]!["description"] = text;
                }

                foreach (var child in obj.Select(p => p.Value).ToList())
                {
                    Trim(child, keep, description);
                }

                break;
        }
    }

    private static RecordedHttp Website(string[]? only = null, string? overviewUpdatedAt = null, Dictionary<string, string>? pageUpdatedAt = null, Dictionary<string, string>? teaserDescription = null)
    {
        var slugs = only ?? Slugs;
        var http = new RecordedHttp().On("current-incidents", Overview(slugs, overviewUpdatedAt, teaserDescription));
        foreach (var slug in slugs)
        {
            var page = TestData.Read("bacs-warnings", slug + ".json");
            if (pageUpdatedAt is not null && pageUpdatedAt.TryGetValue(slug, out var updated))
            {
                var node = JsonNode.Parse(page)!;
                node["updatedAt"] = updated;
                page = node.ToJsonString();
            }

            http.On($"/en/{slug}.json", page);
        }

        return http;
    }
}
