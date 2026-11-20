using System.Text.Json;
using System.Text.Json.Nodes;
using Ardea.Assessment.Rules;
using Ardea.Platform.Persistence;

namespace Ardea.Assessment.Tests;

public sealed class RuleSetFileTests : IDisposable
{
    private static readonly string Shipped = File.ReadAllText(Path.Combine(MasterDataOptions.FindDefault(), RuleSet.Folder, "ruleset-1.json"));

    private readonly string _data = Path.Combine(Path.GetTempPath(), "ardea-rules-" + Guid.CreateVersion7().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_data))
        {
            Directory.Delete(_data, recursive: true);
        }
    }

    [Fact]
    public void The_highest_version_number_is_in_effect_and_other_names_are_ignored()
    {
        Write("ruleset-2.json", WithVersion(2));
        Write("ruleset-10.json", WithVersion(10));
        Write("ruleset-latest.json", "not a rule set");
        Write("ruleset-0.json", "not a rule set either");

        Assert.Equal(10, RuleSet.Load(_data).Version);
    }

    [Fact]
    public void A_file_whose_name_and_version_disagree_is_refused()
    {
        Write("ruleset-3.json", WithVersion(2));

        var error = Assert.Throws<InvalidOperationException>(() => RuleSet.Load(_data));

        Assert.Contains("says version 2; the file name and the version must agree", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_rule_set_the_assessment_does_not_start()
    {
        Assert.Contains("No rule set found", Assert.Throws<InvalidOperationException>(() => RuleSet.Load(_data)).Message, StringComparison.Ordinal);

        Directory.CreateDirectory(Path.Combine(_data, RuleSet.Folder));
        Assert.Contains("No rule set found", Assert.Throws<InvalidOperationException>(() => RuleSet.Load(_data)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("version 0", "version must be 1 or higher")]
    [InlineData("a factor missing", "points.epss is missing")]
    [InlineData("an unknown value", "points.epss.medium is unknown")]
    [InlineData("an unknown factor", "points.reach is an unknown factor")]
    [InlineData("a severity class missing", "severityClasses.low is missing")]
    [InlineData("no step without a minimum", "levels must end with exactly one step without minPoints")]
    [InlineData("a step without a minimum in the middle", "levels must end with exactly one step without minPoints")]
    [InlineData("level 1 from points", "levels from points lie between 2 and 5; level 1 only comes from an exclusion")]
    [InlineData("level 6 from points", "levels from points lie between 2 and 5; level 1 only comes from an exclusion")]
    [InlineData("a cap below 2", "caps lie between 2 and 5")]
    [InlineData("a cap above 5", "caps lie between 2 and 5")]
    [InlineData("a floor above 5", "floors lie between 2 and 5, modelMaxDrop is not negative")]
    [InlineData("a negative drop for the model", "floors lie between 2 and 5, modelMaxDrop is not negative")]
    public void A_broken_rule_set_names_its_problem(string change, string problem)
    {
        var json = JsonNode.Parse(Shipped)!.AsObject();
        var points = json["points"]!.AsObject();
        var levels = json["levels"]!.AsArray();
        switch (change)
        {
            case "version 0": json["version"] = 0; break;
            case "a factor missing": points.Remove("epss"); break;
            case "an unknown value": points["epss"]!["medium"] = 0; break;
            case "an unknown factor": points["reach"] = new JsonObject { ["far"] = 1 }; break;
            case "a severity class missing": json["severityClasses"]!.AsObject().Remove("low"); break;
            case "no step without a minimum": levels[^1]!["minPoints"] = 0; break;
            case "a step without a minimum in the middle": levels[1]!.AsObject().Remove("minPoints"); break;
            case "level 1 from points": levels[^1]!["level"] = 1; break;
            case "level 6 from points": levels[0]!["level"] = 6; break;
            case "a cap below 2": json["caps"]!["development"] = 1; break;
            case "a cap above 5": json["caps"]!["unconfirmed"] = 6; break;
            case "a floor above 5": json["floors"]!["activeInRangeSevere"] = 6; break;
            case "a negative drop for the model": json["floors"]!["modelMaxDrop"] = -1; break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }

        var error = Assert.Throws<InvalidOperationException>(() => RuleSet.Parse(json.ToJsonString()));

        Assert.StartsWith("The rule set is invalid: ", error.Message, StringComparison.Ordinal);
        Assert.Contains(problem, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_file_or_an_unknown_or_missing_member_is_refused()
    {
        Assert.Equal("The rule set is empty.", Assert.Throws<InvalidOperationException>(() => RuleSet.Parse("null")).Message);

        var unknown = JsonNode.Parse(Shipped)!.AsObject();
        unknown["weights"] = 1;
        Assert.Throws<JsonException>(() => RuleSet.Parse(unknown.ToJsonString()));

        var missing = JsonNode.Parse(Shipped)!.AsObject();
        missing.Remove("caps");
        Assert.Throws<JsonException>(() => RuleSet.Parse(missing.ToJsonString()));
    }

    private static string WithVersion(int version)
    {
        var json = JsonNode.Parse(Shipped)!.AsObject();
        json["version"] = version;
        return json.ToJsonString();
    }

    private void Write(string name, string content)
    {
        var folder = Path.Combine(_data, RuleSet.Folder);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), content);
    }
}
