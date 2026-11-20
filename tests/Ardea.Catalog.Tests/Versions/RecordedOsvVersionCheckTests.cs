using System.Text.Json;
using Ardea.Catalog.Contracts;
using Ardea.Catalog.Versions;

namespace Ardea.Catalog.Tests.Versions;

public sealed class RecordedOsvVersionCheckTests
{
    private static readonly string[] Ecosystems = ["npm", "NuGet", "Packagist", "PyPI", "Maven", "Go"];

    public static TheoryData<string, string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var c in Load())
        {
            data.Add(c.GetProperty("ecosystem").GetString()!, c.GetProperty("record").GetString()!, c.GetProperty("installed").GetString()!, c.GetProperty("expected").GetString()!);
        }

        return data;
    }

    [Fact]
    public void Every_ecosystem_has_ten_recorded_cases()
    {
        var counts = Load().GroupBy(c => c.GetProperty("ecosystem").GetString()!).ToDictionary(g => g.Key, g => g.Count());

        Assert.All(Ecosystems, e => Assert.Equal(10, counts.GetValueOrDefault(e)));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Recorded_record_gives_the_expected_verdict(string ecosystem, string record, string installed, string expected)
    {
        var c = Load().Single(x => x.GetProperty("ecosystem").GetString() == ecosystem
            && x.GetProperty("record").GetString() == record
            && x.GetProperty("installed").GetString() == installed);
        var ranges = new List<VersionRange>();
        foreach (var range in c.GetProperty("ranges").EnumerateArray())
        {
            var events = range.GetProperty("events").EnumerateArray()
                .SelectMany(e => e.EnumerateObject().Select(p => KeyValuePair.Create(p.Name, p.Value.GetString()!)));
            ranges.AddRange(VersionRanges.FromOsvEvents(events));
        }

        ranges.AddRange(c.GetProperty("versions").EnumerateArray().Select(v => VersionRange.ExactVersion(v.GetString()!)));
        var scheme = VersionSchemeRegistry.Instance.ForEcosystem(ecosystem);

        var check = VersionCheck.Evaluate(scheme, installed, ranges, VersionStatus.Unknown);

        var verdict = expected switch
        {
            "in_range" => VersionVerdict.InRange,
            "out_of_range" => VersionVerdict.OutOfRange,
            _ => VersionVerdict.Unknown,
        };
        Assert.True(verdict == check.Verdict, $"{record} {installed}: expected {verdict}, got {check.Verdict} ({check.Reason}); {c.GetProperty("note").GetString()}");
    }

    private static List<JsonElement> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Recorded", "osv-version-checks.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return [.. document.RootElement.GetProperty("cases").EnumerateArray().Select(e => e.Clone())];
    }
}
