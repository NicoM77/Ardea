using System.Globalization;

namespace Ardea.Assessment.Rules;

internal sealed record RuleInput
{
    public required bool StrongMatch { get; init; }

    public string? VersionVerdict { get; init; }

    public string? InstalledVersion { get; init; }

    public string? VersionReason { get; init; }

    public bool EntryWithdrawn { get; init; }

    public bool HumanExcluded { get; init; }

    public double? CvssScore { get; init; }

    public string? CvssVersion { get; init; }

    public required string Exploitation { get; init; }

    public string? ExploitationSource { get; init; }

    public DateTimeOffset? ExploitationSince { get; init; }

    public bool AuthorityWarning { get; init; }

    public string? EpssClass { get; init; }

    public required string Criticality { get; init; }

    public required string Exposure { get; init; }

    public required string Protection { get; init; }

    public bool DevelopmentOnly { get; init; }

    public string? HumanVerdict { get; init; }

    public string? ModelVerdict { get; init; }
}

internal sealed record FactorRow(string Factor, string Value, string? Evidence, int Points);

internal sealed record BoundRow(string Bound, int Level);

internal sealed record RuleResult
{
    public required int RulesetVersion { get; init; }

    public required int Level { get; init; }

    public string? Exclusion { get; init; }

    public IReadOnlyList<FactorRow> Factors { get; init; } = [];

    public int? Points { get; init; }

    public int? PointsLevel { get; init; }

    public IReadOnlyList<BoundRow> Caps { get; init; } = [];

    public IReadOnlyList<BoundRow> Floors { get; init; } = [];

    public int? LevelWithoutModel { get; init; }

    public int? PreciseLevel { get; init; }
}

internal static class RuleEngine
{
    public static RuleResult Evaluate(RuleSet rules, RuleInput input)
    {
        var exclusion = !input.StrongMatch ? "weak_match"
            : input.EntryWithdrawn ? "withdrawn"
            : input.HumanExcluded ? "human"
            : input.VersionVerdict == "out_of_range" ? "out_of_range"
            : null;
        if (exclusion is not null)
        {
            return new RuleResult { RulesetVersion = rules.Version, Level = 1, Exclusion = exclusion };
        }

        var inRange = input.VersionVerdict == "in_range";
        var severity = rules.SeverityOf(input.CvssScore);
        var affectedness = inRange || input.HumanVerdict is "affected" or "likely" || input.ModelVerdict is "affected" or "likely" ? "confirmed"
            : input.HumanVerdict == "unlikely" || input.ModelVerdict is "unlikely" or "not_affected" ? "unlikely"
            : "unknown";
        var withoutModel = Points(rules, input, severity, inRange || input.HumanVerdict is "affected" or "likely" ? "confirmed" : input.HumanVerdict == "unlikely" ? "unlikely" : "unknown");
        var factors = Points(rules, input, severity, affectedness);

        var points = factors.Sum(f => f.Points);
        var pointsLevel = rules.LevelOf(points);
        var level = pointsLevel;
        var caps = new List<BoundRow>();
        var floors = new List<BoundRow>();

        var confirmed = inRange || input.HumanVerdict is "affected" or "likely";
        if (!confirmed && level > rules.Caps.Unconfirmed)
        {
            level = rules.Caps.Unconfirmed;
            caps.Add(new BoundRow("unconfirmed", level));
        }

        if (input.DevelopmentOnly && level > rules.Caps.Development)
        {
            level = rules.Caps.Development;
            caps.Add(new BoundRow("development", level));
        }

        void Floor(string name, int minimum)
        {
            if (level < minimum)
            {
                level = minimum;
                floors.Add(new BoundRow(name, minimum));
            }
        }

        if (input.Exploitation == "active")
        {
            Floor("active", rules.Floors.Active);
            if (inRange)
            {
                Floor("active_in_range", rules.Floors.ActiveInRange);
                if (severity is "high" or "critical" && input.Exposure != "isolated")
                {
                    Floor("active_in_range_severe", rules.Floors.ActiveInRangeSevere);
                }
            }
        }

        int? reference = null;
        if (input.ModelVerdict is not null && input.HumanVerdict is null)
        {
            reference = Bounded(rules, input, rules.LevelOf(withoutModel.Sum(f => f.Points)), confirmed, severity, inRange);
            Floor("model_max_drop", reference.Value - rules.Floors.ModelMaxDrop);
        }

        return new RuleResult
        {
            RulesetVersion = rules.Version,
            Level = level,
            LevelWithoutModel = reference,
            Factors = factors,
            Points = points,
            PointsLevel = pointsLevel,
            Caps = caps,
            Floors = floors,
        };
    }

    private static int Bounded(RuleSet rules, RuleInput input, int level, bool confirmed, string severity, bool inRange)
    {
        if (!confirmed)
        {
            level = Math.Min(level, rules.Caps.Unconfirmed);
        }

        if (input.DevelopmentOnly)
        {
            level = Math.Min(level, rules.Caps.Development);
        }

        if (input.Exploitation == "active")
        {
            level = Math.Max(level, rules.Floors.Active);
            if (inRange)
            {
                level = Math.Max(level, rules.Floors.ActiveInRange);
                if (severity is "high" or "critical" && input.Exposure != "isolated")
                {
                    level = Math.Max(level, rules.Floors.ActiveInRangeSevere);
                }
            }
        }

        return level;
    }

    private static List<FactorRow> Points(RuleSet rules, RuleInput input, string severity, string affectedness)
    {
        var exploitation = input.Exploitation is "active" or "poc" ? input.Exploitation : "none";
        var epss = input.EpssClass == "high" ? "high" : "low";
        var authority = input.AuthorityWarning ? "yes" : "no";
        var protection = input.Protection == "effective" ? "effective" : "none";
        return
        [
            Row(rules, "severity", severity, input.CvssScore is { } score
                ? $"CVSS {input.CvssVersion ?? "?"}: {score.ToString("0.0", CultureInfo.InvariantCulture)}"
                : "no score"),
            Row(rules, "exploitation", exploitation, exploitation == "none" ? null
                : string.Join(' ', new[] { input.ExploitationSource, input.ExploitationSince is { } since ? "since " + since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null }.OfType<string>()) is { Length: > 0 } text ? text : null),
            Row(rules, "affectedness", affectedness, AffectednessEvidence(input)),
            Row(rules, "criticality", input.Criticality, null),
            Row(rules, "exposure", input.Exposure, null),
            Row(rules, "protection", protection, null),
            Row(rules, "authorityWarning", authority, null),
            Row(rules, "epss", epss, null),
        ];
    }

    private static string? AffectednessEvidence(RuleInput input)
    {
        var version = input.InstalledVersion is null ? "version not recorded" : $"version {input.InstalledVersion}";
        var check = input.VersionVerdict switch
        {
            "in_range" => $"{version} in an affected range",
            "unknown" => $"{version}, affectedness unknown ({input.VersionReason ?? "no comparable range"})",
            _ => null,
        };
        var verdict = input.HumanVerdict is not null ? $"person: {input.HumanVerdict}" : input.ModelVerdict is not null ? $"model: {input.ModelVerdict}" : null;
        return string.Join("; ", new[] { check, verdict }.OfType<string>()) is { Length: > 0 } text ? text : null;
    }

    private static FactorRow Row(RuleSet rules, string factor, string value, string? evidence) =>
        new(factor, value, evidence, rules.PointsOf(factor, value));
}
