using System.Text.Json;
using Ardea.Catalog;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sources.Formats.Osv;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CatalogRange = Ardea.Catalog.Contracts.VersionRange;

namespace Ardea.Sources.Tests.Osv;

/// <summary>
/// Phase 1 acceptance (master plan 14): for the six ecosystems the version check gives the expected
/// result on ten recorded records each. The cases of <c>Ardea.Catalog.Tests/Recorded/osv-version-checks.json</c>
/// are run here on the adapter's output: the whole recorded record goes through the reader, and its
/// ranges and version scheme for the package reach the catalogue's check the way the core stores them.
/// </summary>
public sealed class OsvVersionCheckTests
{
    private static readonly IVersionSchemes Schemes = VersionSchemesOfTheCatalogue();

    public static TheoryData<int> Cases => [.. Enumerable.Range(0, Load().Count)];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Adapter_ranges_give_the_recorded_verdict(int index)
    {
        var c = Load()[index];
        var ecosystem = c.GetProperty("ecosystem").GetString()!;
        var record = c.GetProperty("record").GetString()!;
        var installed = c.GetProperty("installed").GetString()!;
        var products = Products(ecosystem, record, c.GetProperty("package").GetString()!);
        var ranges = products.SelectMany(p => p.Versions).Select(ToCatalog).ToList();

        // The scheme the adapter names is the one the catalogue gives the ecosystem.
        var schemeName = Assert.Single(products.Select(p => p.VersionScheme).Distinct());
        var scheme = Schemes.Find(schemeName);
        Assert.NotNull(scheme);
        Assert.Equal(Schemes.ForEcosystem(ecosystem)!.Name, scheme.Name);

        // OSV names no default status for unnamed versions.
        var check = VersionCheck.Evaluate(scheme, installed, ranges, VersionStatus.Unknown);

        var expected = c.GetProperty("expected").GetString() switch
        {
            "in_range" => VersionVerdict.InRange,
            "out_of_range" => VersionVerdict.OutOfRange,
            _ => VersionVerdict.Unknown,
        };
        Assert.True(expected == check.Verdict, $"{record} {installed}: expected {expected}, got {check.Verdict} ({check.Reason}); {c.GetProperty("note").GetString()}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Adapter_ranges_are_the_ranges_the_case_file_copied_from_the_record(int index)
    {
        var c = Load()[index];
        var products = Products(c.GetProperty("ecosystem").GetString()!, c.GetProperty("record").GetString()!, c.GetProperty("package").GetString()!);

        var fromCase = c.GetProperty("ranges").EnumerateArray()
            .SelectMany(r => VersionRanges.FromOsvEvents(r.GetProperty("events").EnumerateArray()
                .SelectMany(e => e.EnumerateObject().Select(p => KeyValuePair.Create(p.Name, p.Value.GetString()!)))))
            .Select(r => (r.Introduced, r.Fixed, r.LastAffected, r.Exact));
        var fromAdapter = products.SelectMany(p => p.Versions).Select(r => (r.Introduced, r.Fixed, r.LastAffected, r.Exact));

        Assert.Equal(fromCase, fromAdapter);
    }

    [Fact]
    public void Every_ecosystem_has_ten_cases()
    {
        var counts = Load().GroupBy(c => c.GetProperty("ecosystem").GetString()!).ToDictionary(g => g.Key, g => g.Count());

        Assert.All(OsvEcosystems.Mvp, e => Assert.Equal(10, counts.GetValueOrDefault(e)));
    }

    /// <summary>The products of the recorded record for the case's package, matched by package URL as the catalogue compares them.</summary>
    private static List<AffectedProduct> Products(string ecosystem, string record, string package)
    {
        var result = OsvRecordReader.Read(TestData.Read("osv", $"{ecosystem}.{record}.json"));
        Assert.Null(result.QuarantineReason);
        var wanted = PackageCoordinate.Normalize(ecosystem, package);
        var products = result.Observation!.Claims.SelectMany(b => b.Affected)
            .Where(a => PackageCoordinate.TryParsePurl(a.Purl, out var coordinate, out _) && coordinate == wanted)
            .ToList();
        Assert.NotEmpty(products);
        return products;
    }

    /// <summary>As the core stores an affected range (<c>entry_affected</c>) and the assessment reads it back.</summary>
    private static CatalogRange ToCatalog(Intel.Sources.Abstractions.VersionRange range) => new()
    {
        Introduced = range.Introduced,
        Fixed = range.Fixed,
        LastAffected = range.LastAffected,
        Exact = range.Exact,
        Scheme = range.Scheme,
        Status = range.Status switch
        {
            AffectedStatus.Affected => VersionStatus.Affected,
            AffectedStatus.Unaffected => VersionStatus.Unaffected,
            _ => VersionStatus.Unknown,
        },
    };

    private static IVersionSchemes VersionSchemesOfTheCatalogue()
    {
        var services = new ServiceCollection();
        new CatalogModule().AddServices(services, new ConfigurationBuilder().Build());
        return services.BuildServiceProvider().GetRequiredService<IVersionSchemes>();
    }

    private static List<JsonElement> Load()
    {
        using var document = JsonDocument.Parse(TestData.Read("catalog", "osv-version-checks.json"));
        return [.. document.RootElement.GetProperty("cases").EnumerateArray().Select(e => e.Clone())];
    }
}
