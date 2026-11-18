using System.Text.Json;
using System.Text.Json.Serialization;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sources.Formats.CveJson5;

namespace Ardea.Sources.Tests.CveProgram;

/// <summary>The CVE JSON 5 reader against recorded real records; the output is a reviewed snapshot.</summary>
public sealed class CveRecordReaderTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static TheoryData<string> Records => [.. TestData.Files("cve-program", "CVE-*.json")];

    [Theory]
    [MemberData(nameof(Records))]
    public void Recorded_record_reads_into_the_reviewed_observation(string file)
    {
        var result = CveRecordReader.Read(TestData.Read("cve-program", file));

        Assert.Null(result.QuarantineReason);
        Snapshot.Match(JsonSerializer.Serialize(result.Observation, Json), "cve-program." + Path.GetFileNameWithoutExtension(file));
    }

    [Fact]
    public void Kev_listing_in_the_cisa_container_means_active_exploitation()
    {
        var claim = Read("CVE-2021-44228.json");

        Assert.Equal(ExploitationState.Active, claim.Exploitation!.State);
        Assert.Equal(new DateTimeOffset(2021, 12, 10, 0, 0, 0, TimeSpan.Zero), claim.Exploitation.Since);
        Assert.Equal("active", claim.Exploitation.Ssvc["Exploitation"]);
        Assert.Contains(claim.Metrics, m => m.Assigner == "CISA-ADP" && m.Version == "3.1" && m.Score == 10);
    }

    [Fact]
    public void Git_ranges_are_kept_with_their_scheme_and_version_numbers_beside_them()
    {
        var claim = Read("CVE-2024-26581.json");
        var linux = claim.Affected.Where(a => a.Product == "Linux").ToList();

        Assert.Contains(linux.SelectMany(a => a.Versions), v => v.Scheme == "git" && v.Introduced is not null && v.Fixed is not null);
        Assert.Contains(linux.SelectMany(a => a.Versions), v => v.Scheme is "semver" or "original_commit_for_fix" || v.Scheme is null);
        Assert.Contains(linux, a => a.DefaultStatus == AffectedStatus.Unaffected);
    }

    [Fact]
    public void Status_changes_inside_a_range_split_it_and_a_wildcard_bound_means_none()
    {
        var ranges = Read("CVE-2021-44228.json").Affected.SelectMany(a => a.Versions).ToList();

        Assert.Equal(
            [
                ("2.0-beta9", "2.3.1", AffectedStatus.Affected),
                ("2.3.1", "2.4", AffectedStatus.Unaffected),
                ("2.4", "2.12.2", AffectedStatus.Affected),
                ("2.12.2", "2.13.0", AffectedStatus.Unaffected),
                ("2.13.0", "2.15.0", AffectedStatus.Affected),
                ("2.15.0", null, AffectedStatus.Unaffected),
            ],
            ranges.Select(r => (r.Introduced!, r.Fixed, r.Status)).ToList());
    }

    [Fact]
    public void Inclusive_upper_bound_becomes_last_affected()
    {
        var claim = Read("CVE-2026-120049.json");

        Assert.Contains(claim.Affected.SelectMany(a => a.Versions), v => v.LastAffected is not null && v.Fixed is null);
    }

    [Fact]
    public void Rejected_record_carries_its_reason_and_state()
    {
        var claim = Read("CVE-2026-0000-rejected.json");

        Assert.Equal(VulnerabilityState.Rejected, claim.State);
        Assert.StartsWith("This candidate was withdrawn", claim.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_without_cve_id_or_of_another_major_version_goes_to_quarantine()
    {
        Assert.NotNull(CveRecordReader.Read("""{"dataVersion":"5.1"}""").QuarantineReason);
        Assert.NotNull(CveRecordReader.Read("""{"dataVersion":"4.0","cveMetadata":{"cveId":"CVE-2020-0001"}}""").QuarantineReason);
        Assert.NotNull(CveRecordReader.Read("not json").QuarantineReason);
    }

    [Fact]
    public void Missing_information_is_not_guessed()
    {
        // n/a vendors are dropped; an absent default status stays absent (master plan 6.1, 6.5).
        var claims = TestData.Files("cve-program", "CVE-*.json").Select(Read).ToList();

        Assert.DoesNotContain(claims.SelectMany(c => c.Affected), a => a.Vendor is "n/a" or "N/A");
        Assert.Contains(claims.SelectMany(c => c.Affected), a => a.DefaultStatus is null);
    }

    [Theory]
    [InlineData("https://registry.npmjs.org", "express", "pkg:npm/express")]
    [InlineData("https://registry.npmjs.org", "@angular/core", "pkg:npm/%40angular/core")]
    [InlineData("https://pypi.org", "Django_Rest", "pkg:pypi/django-rest")]
    [InlineData("https://repo.maven.apache.org/maven2", "org.apache.logging.log4j:log4j-core", "pkg:maven/org.apache.logging.log4j/log4j-core")]
    [InlineData("https://www.nuget.org", "Newtonsoft.Json", "pkg:nuget/Newtonsoft.Json")]
    [InlineData("https://packagist.org", "symfony/http-kernel", "pkg:composer/symfony/http-kernel")]
    [InlineData("https://pkg.go.dev", "golang.org/x/net", "pkg:golang/golang.org/x/net")]
    [InlineData("https://example.com/downloads", "thing", null)]
    [InlineData("https://repo.maven.apache.org/maven2", "no-group", null)]
    public void Package_url_is_derived_for_the_known_registries_only(string collection, string package, string? expected)
    {
        Assert.Equal(expected, PackageUrl.From(collection, package));
    }

    private static ClaimBlock Read(string file) =>
        Assert.Single(CveRecordReader.Read(TestData.Read("cve-program", file)).Observation!.Claims);
}
