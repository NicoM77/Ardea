using System.Globalization;

namespace Ardea.Assessment.Rules;

/// <summary>
/// Everything the level of one finding depends on (master plan 7.1 property 2: recomputable from stored
/// inputs). Values are the stored ones: entry, profile, match, version check and, where present, a
/// verdict of the model or a person.
/// </summary>
internal sealed record RuleInput
{
    /// <summary>A strong hit (package, CPE, product); a weak one is excluded.</summary>
    public required bool StrongMatch { get; init; }

    /// <summary><c>in_range</c>, <c>out_of_range</c>, <c>unknown</c>; <c>null</c> for a weak hit.</summary>
    public string? VersionVerdict { get; init; }

    /// <summary>The installed version, for the evidence.</summary>
    public string? InstalledVersion { get; init; }

    /// <summary>The rule of the version check, for the evidence.</summary>
    public string? VersionReason { get; init; }

    /// <summary>The entry was withdrawn by its issuer or merged into another.</summary>
    public bool EntryWithdrawn { get; init; }

    /// <summary>A person set the finding to «not affected».</summary>
    public bool HumanExcluded { get; init; }

    public double? CvssScore { get; init; }

    public string? CvssVersion { get; init; }

    /// <summary><c>none</c>, <c>poc</c> or <c>active</c>.</summary>
    public required string Exploitation { get; init; }

    public string? ExploitationSource { get; init; }

    public DateTimeOffset? ExploitationSince { get; init; }

    public bool AuthorityWarning { get; init; }

    /// <summary><c>high</c>, <c>low</c> or <c>null</c>.</summary>
    public string? EpssClass { get; init; }

    /// <summary><c>high</c>, <c>normal</c> or <c>low</c>.</summary>
    public required string Criticality { get; init; }

    /// <summary><c>internet</c>, <c>internal</c> or <c>isolated</c>.</summary>
    public required string Exposure { get; init; }

    /// <summary><c>none</c> or <c>effective</c>.</summary>
    public required string Protection { get; init; }

    /// <summary>The component is only used in development (master plan 16.5) and no person said otherwise.</summary>
    public bool DevelopmentOnly { get; init; }

    /// <summary>A person's verdict on affectedness: <c>affected</c>, <c>likely</c>, <c>unlikely</c>; <c>null</c> for none.</summary>
    public string? HumanVerdict { get; init; }

    /// <summary>The model's verdict (phase 4): <c>affected</c>, <c>likely</c>, <c>uncertain</c>, <c>unlikely</c>, <c>not_affected</c>.</summary>
    public string? ModelVerdict { get; init; }
}

/// <summary>One row of the explanation: the factor, its value, its evidence and its effect.</summary>
internal sealed record FactorRow(string Factor, string Value, string? Evidence, int Points);

/// <summary>An upper or lower bound that changed the level.</summary>
internal sealed record BoundRow(string Bound, int Level);

/// <summary>The level of a finding with its explanation, stored as <c>finding.factors</c>.</summary>
internal sealed record RuleResult
{
    public required int RulesetVersion { get; init; }

    public required int Level { get; init; }

    /// <summary>Why the level is 1 without computation: <c>weak_match</c>, <c>out_of_range</c>, <c>withdrawn</c>, <c>human</c>.</summary>
    public string? Exclusion { get; init; }

    public IReadOnlyList<FactorRow> Factors { get; init; } = [];

    public int? Points { get; init; }

    /// <summary>The level from the points alone.</summary>
    public int? PointsLevel { get; init; }

    public IReadOnlyList<BoundRow> Caps { get; init; } = [];

    public IReadOnlyList<BoundRow> Floors { get; init; } = [];
}

/// <summary>
/// The level of one finding after rule set version 1 (master plan 7.6), in fixed order: exclusion,
/// points, upper bounds, lower bounds. Pure: the same input and rule set give the same result.
/// </summary>
internal static class RuleEngine
{
    public static RuleResult Evaluate(RuleSet rules, RuleInput input)
    {
        // 1. Exclusion: level 1 without computation, always from structured data or a person; before every bound.
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

        // 2. Points.
        var points = factors.Sum(f => f.Points);
        var pointsLevel = rules.LevelOf(points);
        var level = pointsLevel;
        var caps = new List<BoundRow>();
        var floors = new List<BoundRow>();

        // 3. Upper bounds: «critical» only for what the version check or a person confirmed; development-only components.
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

        // 4. Lower bounds that no other factor undercuts.
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

        // The model lowers by at most one level against the result without it, until a person confirms it.
        if (input.ModelVerdict is not null && input.HumanVerdict is null)
        {
            var reference = Bounded(rules, input, rules.LevelOf(withoutModel.Sum(f => f.Points)), confirmed, severity, inRange);
            Floor("model_max_drop", reference - rules.Floors.ModelMaxDrop);
        }

        return new RuleResult
        {
            RulesetVersion = rules.Version,
            Level = level,
            Factors = factors,
            Points = points,
            PointsLevel = pointsLevel,
            Caps = caps,
            Floors = floors,
        };
    }

    /// <summary>The level of a sum after the same bounds, for the comparison with the result without the model.</summary>
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
