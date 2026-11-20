using System.Text.Json;
using System.Text.Json.Serialization;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sources.Formats.Osv;

namespace Ardea.Sources.Tests.Osv;

public sealed class OsvRecordReaderTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static TheoryData<string> Records => [.. TestData.Files("osv", "*.json")];

    [Theory]
    [MemberData(nameof(Records))]
    public void Recorded_record_reads_into_the_reviewed_observation(string file)
    {
        var result = OsvRecordReader.Read(TestData.Read("osv", file));

        Assert.Null(result.QuarantineReason);
        Snapshot.Match(JsonSerializer.Serialize(result.Observation, Json), "osv." + Path.GetFileNameWithoutExtension(file));
    }

    [Fact]
    public void Every_ecosystem_has_at_least_two_recorded_records()
    {
        var counts = TestData.Files("osv", "*.json").GroupBy(f => f[..f.IndexOf('.', StringComparison.Ordinal)]).ToDictionary(g => g.Key, g => g.Count());

        Assert.All(OsvEcosystems.Mvp, e => Assert.True(counts.GetValueOrDefault(e) >= 2, $"{e}: {counts.GetValueOrDefault(e)} records"));
    }

    [Fact]
    public void A_ghsa_record_first_without_and_later_with_its_cve_id_carries_the_cve_as_alias()
    {
        var first = Read("Go.GHSA-pfrf-9r5f-73f5.ghsa-only.json");
        var later = Read("Go.GHSA-pfrf-9r5f-73f5.json");

        Assert.Equal([new Identifier("GHSA", "GHSA-pfrf-9r5f-73f5")], first.Aliases);
        Assert.Equal(
            [new Identifier("GHSA", "GHSA-pfrf-9r5f-73f5"), new Identifier("CVE", "CVE-2026-29067"), new Identifier("OSV", "GO-2025-4212")],
            later.Aliases);
        Assert.All(first.Affected, a => Assert.StartsWith("pkg:golang/github.com/zitadel/zitadel", a.Purl, StringComparison.Ordinal));
    }

    [Fact]
    public void Go_versions_keep_incompatible_and_pseudo_versions_and_one_range_object_can_hold_several_ranges()
    {
        var claim = Read("Go.GO-2026-6547.json");
        var server = claim.Affected.Single(a => a.Product == "github.com/mattermost/mattermost-server");
        var v8 = claim.Affected.Single(a => a.Product == "github.com/mattermost/mattermost/server/v8");

        Assert.Equal(
            [("10.11.0+incompatible", "10.11.18+incompatible"), ("11.7.0+incompatible", "11.7.1+incompatible")],
            server.Versions.Select(v => (v.Introduced, v.Fixed)));
        Assert.Equal("8.0.0-20260518160100-9bd77d3fc4d2", Assert.Single(v8.Versions).Fixed);
        Assert.All(claim.Affected.SelectMany(a => a.Versions), v => Assert.Equal("go", v.Scheme));
        var v5 = claim.Affected.Single(a => a.Product == "github.com/mattermost/mattermost-server/v5");
        Assert.Equal(new VersionRange { Scheme = "go", Raw = v5.Versions[0].Raw }, Assert.Single(v5.Versions));
        Assert.Equal([new Identifier("OSV", "GO-2026-6547"), new Identifier("CVE", "CVE-2026-8823"), new Identifier("GHSA", "GHSA-xmfh-3ccg-c9fx")], claim.Aliases);
    }

    [Fact]
    public void A_versions_list_counts_only_where_the_entry_names_no_comparable_range()
    {
        var envoy = Read("Go.GHSA-3cw6-2j68-868p.json").Affected;
        var invokeai = Assert.Single(Read("PyPI.PYSEC-2025-9.json").Affected);

        Assert.Equal("1.37.0", Assert.Single(envoy[0].Versions).Exact);
        Assert.Equal(("1.36.0", "1.36.4"), (envoy[1].Versions[0].Introduced, envoy[1].Versions[0].LastAffected));
        Assert.DoesNotContain(invokeai.Versions, v => v.Exact is not null);
        Assert.Equal(
            [("git", null, "756008dc5899081c5aa51e5bd8f24c1b3975a59e"), ("pep440", "5.3.1", "5.4.3rc1")],
            invokeai.Versions.Select(v => (v.Scheme, v.Introduced, v.Fixed)));
    }

    [Fact]
    public void A_withdrawn_record_is_rejected()
    {
        var claim = Read("NuGet.GHSA-wrm2-c2h2-4w82.json");

        Assert.Equal(VulnerabilityState.Rejected, claim.State);
        Assert.Equal(VulnerabilityState.Rejected, Read("Maven.GHSA-22c5-cpvr-cfvq.json").State);
        Assert.Equal(VulnerabilityState.Published, Read("npm.GHSA-rv95-896h-c2vc.json").State);
    }

    [Fact]
    public void Each_package_gets_the_scheme_and_package_url_of_its_own_ecosystem()
    {
        var claim = Read("Maven.GHSA-xc7w-r669-48pf.json");

        Assert.Contains(claim.Affected, a => a.Purl!.StartsWith("pkg:maven/", StringComparison.Ordinal) && a.VersionScheme == "maven");
        Assert.Contains(claim.Affected, a => a.Purl!.StartsWith("pkg:pypi/", StringComparison.Ordinal) && a.VersionScheme == "pep440");
        Assert.Equal("pkg:composer/drupal/core", Assert.Single(Read("Packagist.DRUPAL-CORE-2026-003.json").Affected).Purl);
    }

    [Fact]
    public void Severity_keeps_the_vector_and_leaves_the_score_to_be_computed()
    {
        var claim = Read("npm.GHSA-rv95-896h-c2vc.json");

        var metric = Assert.Single(claim.Metrics);
        Assert.Equal(new SeverityMetric("CVSS", "3.1", "CVSS:3.1/AV:N/AC:L/PR:N/UI:R/S:C/C:L/I:L/A:N", null) { Assigner = "GitHub" }, metric);
        Assert.Equal(["CWE-1286", "CWE-601"], claim.Weaknesses);
        Assert.True(claim.Remediation!.FixAvailable);
        Assert.Contains(claim.References, r => r.Url.AbsoluteUri == "https://github.com/expressjs/express" && r.Tags.SequenceEqual(["package"]));
    }

    [Theory]
    [InlineData("not json", "Not valid JSON")]
    [InlineData("""{"modified":"2026-01-01T00:00:00Z"}""", "No id")]
    [InlineData("""{"id":"GHSA-aaaa-bbbb-cccc","schema_version":"2.0.0","modified":"2026-01-01T00:00:00Z"}""", "unsupported schema_version")]
    [InlineData("""{"id":"GHSA-aaaa-bbbb-cccc","modified":"yesterday"}""", "no valid modified")]
    public void An_invalid_record_goes_to_quarantine(string json, string reason)
    {
        var result = OsvRecordReader.Read(json);

        Assert.Null(result.Observation);
        Assert.Contains(reason, result.QuarantineReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-11-17T21:00:04.02163339Z", "2026-11-17T21:00:04.0216333+00:00")]
    [InlineData("2026-11-18T19:45:04.566522493Z", "2026-11-18T19:45:04.5665224+00:00")]
    [InlineData("2024-03-25T19:40:26Z", "2024-03-25T19:40:26.0000000+00:00")]
    public void Timestamps_with_nine_fractional_digits_are_cut_to_seven(string text, string expected)
    {
        Assert.True(OsvTimestamp.TryParse(text, out var value));
        Assert.Equal(expected, value.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("npm", "@angular/core", "pkg:npm/%40angular/core")]
    [InlineData("Maven", "org.springframework:spring-web", "pkg:maven/org.springframework/spring-web")]
    [InlineData("PyPI", "Django_Rest.framework", "pkg:pypi/django-rest-framework")]
    [InlineData("Packagist", "symfony/http-kernel", "pkg:composer/symfony/http-kernel")]
    [InlineData("Go", "golang.org/x/net", "pkg:golang/golang.org/x/net")]
    [InlineData("NuGet", "System.Text.Json", "pkg:nuget/System.Text.Json")]
    [InlineData("Debian:12", "openssl", null)]
    [InlineData("Maven", "no-group", null)]
    public void A_package_url_is_built_from_ecosystem_and_name_when_the_record_has_none(string ecosystem, string name, string? expected)
    {
        Assert.Equal(expected, OsvEcosystems.PackageUrl(ecosystem, name));
    }

    private static ClaimBlock Read(string file)
    {
        var result = OsvRecordReader.Read(TestData.Read("osv", file));
        Assert.Null(result.QuarantineReason);
        return Assert.Single(result.Observation!.Claims);
    }
}
