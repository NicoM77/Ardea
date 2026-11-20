using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ardea.Assessment.Rules;

internal sealed record RuleSet
{
    public const string Folder = "rules";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public required int Version { get; init; }

    public string? Description { get; init; }

    public required Dictionary<string, double> SeverityClasses { get; init; }

    public required Dictionary<string, Dictionary<string, int>> Points { get; init; }

    public required List<LevelStep> Levels { get; init; }

    public required CapRules Caps { get; init; }

    public required FloorRules Floors { get; init; }

    [JsonPropertyName("deadlineHours")]
    public JsonElement? RetiredDeadlineHours { get; init; }

    public static readonly IReadOnlyList<string> Factors =
        ["severity", "exploitation", "affectedness", "criticality", "exposure", "protection", "authorityWarning", "epss"];

    public static RuleSet Load(string dataDirectory)
    {
        var folder = Path.Combine(dataDirectory, Folder);
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "ruleset-*.json") : [];
        var latest = files
            .Select(f => (File: f, Version: int.TryParse(Path.GetFileNameWithoutExtension(f)["ruleset-".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : -1))
            .Where(f => f.Version > 0)
            .OrderByDescending(f => f.Version)
            .FirstOrDefault();
        if (latest.File is null)
        {
            throw new InvalidOperationException($"No rule set found in {folder}; the image must carry data/rules/ruleset-1.json (master plan 4.9).");
        }

        var ruleSet = Parse(File.ReadAllText(latest.File));
        if (ruleSet.Version != latest.Version)
        {
            throw new InvalidOperationException($"{latest.File} says version {ruleSet.Version}; the file name and the version must agree.");
        }

        return ruleSet;
    }

    public static RuleSet Parse(string json)
    {
        var ruleSet = JsonSerializer.Deserialize<RuleSet>(json, Json) ?? throw new InvalidOperationException("The rule set is empty.");
        var problems = ruleSet.Check();
        return problems.Count == 0 ? ruleSet : throw new InvalidOperationException("The rule set is invalid: " + string.Join("; ", problems));
    }

    public int PointsOf(string factor, string value) => Points[factor][value];

    public int LevelOf(int points) => Levels.First(l => l.MinPoints is null || points >= l.MinPoints).Level;

    public string SeverityOf(double? score) =>
        score is not { } value ? "unrated"
        : SeverityClasses.Where(c => value >= c.Value).OrderByDescending(c => c.Value).Select(c => c.Key).FirstOrDefault() ?? "low";

    private List<string> Check()
    {
        var problems = new List<string>();
        if (Version < 1)
        {
            problems.Add("version must be 1 or higher");
        }

        var values = new Dictionary<string, string[]>
        {
            ["severity"] = ["critical", "high", "medium", "low", "unrated"],
            ["exploitation"] = ["active", "poc", "none"],
            ["affectedness"] = ["confirmed", "unknown", "unlikely"],
            ["criticality"] = ["high", "normal", "low"],
            ["exposure"] = ["internet", "internal", "isolated"],
            ["protection"] = ["effective", "none"],
            ["authorityWarning"] = ["yes", "no"],
            ["epss"] = ["high", "low"],
        };
        foreach (var (factor, expected) in values)
        {
            if (!Points.TryGetValue(factor, out var points))
            {
                problems.Add($"points.{factor} is missing");
                continue;
            }

            problems.AddRange(expected.Where(v => !points.ContainsKey(v)).Select(v => $"points.{factor}.{v} is missing"));
            problems.AddRange(points.Keys.Where(v => !expected.Contains(v)).Select(v => $"points.{factor}.{v} is unknown"));
        }

        problems.AddRange(Points.Keys.Where(f => !values.ContainsKey(f)).Select(f => $"points.{f} is an unknown factor"));
        problems.AddRange(new[] { "critical", "high", "medium", "low" }.Where(c => !SeverityClasses.ContainsKey(c)).Select(c => $"severityClasses.{c} is missing"));

        if (Levels.Count == 0 || Levels[^1].MinPoints is not null || Levels.Take(Levels.Count - 1).Any(l => l.MinPoints is null))
        {
            problems.Add("levels must end with exactly one step without minPoints");
        }

        for (var i = 1; i < Levels.Count; i++)
        {
            if (Levels[i].Level >= Levels[i - 1].Level || (Levels[i].MinPoints is { } min && min >= Levels[i - 1].MinPoints))
            {
                problems.Add("levels must go down in level and in minPoints");
            }
        }

        if (Levels.Exists(l => l.Level is < 2 or > 5))
        {
            problems.Add("levels from points lie between 2 and 5; level 1 only comes from an exclusion");
        }

        if (Caps.Unconfirmed is < 2 or > 5 || Caps.Development is < 2 or > 5)
        {
            problems.Add("caps lie between 2 and 5");
        }

        if (Floors.Active is < 2 or > 5 || Floors.ActiveInRange is < 2 or > 5 || Floors.ActiveInRangeSevere is < 2 or > 5 || Floors.ModelMaxDrop < 0)
        {
            problems.Add("floors lie between 2 and 5, modelMaxDrop is not negative");
        }

        return problems;
    }
}

internal sealed record LevelStep(int? MinPoints, int Level);

internal sealed record CapRules(int Unconfirmed, int Development);

internal sealed record FloorRules(int Active, int ActiveInRange, int ActiveInRangeSevere, int ModelMaxDrop);
