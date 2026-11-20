using System.Text.Json;
using Ardea.Assessment.Assessing;
using Ardea.Assessment.Rules;
using Ardea.Platform.Persistence;

namespace Ardea.Assessment.Tests;

/// <summary>Rule set version 1 (task 3.4; master plan 7.6): factors, bounds and the worked examples.</summary>
public sealed class RuleSetTests
{
    private static readonly RuleSet Rules = RuleSet.Load(MasterDataOptions.FindDefault());

    /// <summary>A strong hit in range on a normal, internal system without signals; each case changes what it names.</summary>
    private static RuleInput Base => new()
    {
        StrongMatch = true,
        VersionVerdict = "in_range",
        InstalledVersion = "1.0",
        CvssScore = 7.5,
        CvssVersion = "3.1",
        Exploitation = "none",
        Criticality = "normal",
        Exposure = "internal",
        Protection = "none",
    };

    /// <summary>The worked examples of master plan 7.6 (phase 3 acceptance): summands severity, exploitation, affectedness, criticality, exposure.</summary>
    public static TheoryData<string, double, string, string, string, int, int> Examples => new()
    {
        { "Puppet Enterprise, CVE-2026-115731", 9.4, "active", "high", "internal", 9, 5 },
        { "the same before KEV", 9.4, "none", "high", "internal", 6, 4 },
        { "PHP, CVE-2026-109619", 9.8, "active", "normal", "internet", 9, 5 },
        { "PostgreSQL, CVE-2026-116325", 8.8, "none", "high", "internal", 5, 4 },
        { "MySQL Server, CVE-2026-114952", 9.1, "none", "normal", "internal", 5, 4 },
        { "Umbraco CMS, CVE-2026-130068", 6.1, "none", "normal", "internet", 4, 3 },
        { "ingress-nginx, CVE-2026-116472", 3.1, "none", "high", "internal", 3, 2 },
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public void The_worked_examples_give_the_stated_levels(string example, double score, string exploitation, string criticality, string exposure, int points, int level)
    {
        var result = RuleEngine.Evaluate(Rules, Base with { CvssScore = score, Exploitation = exploitation, Criticality = criticality, Exposure = exposure });

        Assert.True(points == result.Points, $"{example}: {result.Points} points");
        Assert.True(level == result.Level, $"{example}: level {result.Level}");
        Assert.Equal(RuleSet.Factors, result.Factors.Select(f => f.Factor));
    }

    [Fact]
    public void A_variant_is_level_1_without_computation()
    {
        // SQL Server, CVE-2026-67374: Azure SQL is a variant and not named as affected.
        var result = RuleEngine.Evaluate(Rules, Base with { StrongMatch = false, VersionVerdict = null, CvssScore = 9.8, Exploitation = "active" });

        Assert.Equal(1, result.Level);
        Assert.Equal("weak_match", result.Exclusion);
        Assert.Empty(result.Factors);
    }

    [Fact]
    public void An_exclusion_goes_before_every_floor()
    {
        foreach (var input in new[]
        {
            Base with { VersionVerdict = "out_of_range" },
            Base with { EntryWithdrawn = true },
            Base with { HumanExcluded = true },
        })
        {
            Assert.Equal(1, RuleEngine.Evaluate(Rules, input with { CvssScore = 10, Exploitation = "active", Exposure = "internet" }).Level);
        }
    }

    [Fact]
    public void Critical_is_reserved_for_confirmed_affectedness()
    {
        var unknown = RuleEngine.Evaluate(Rules, Base with { VersionVerdict = "unknown", CvssScore = 9.8, Exploitation = "poc", Exposure = "internet", Criticality = "high" });
        Assert.Equal(8, unknown.Points);
        Assert.Equal(4, unknown.Level);
        Assert.Equal([new BoundRow("unconfirmed", 4)], unknown.Caps);

        var confirmedByPerson = RuleEngine.Evaluate(Rules, Base with { VersionVerdict = "unknown", HumanVerdict = "affected", CvssScore = 9.8, Exploitation = "poc", Exposure = "internet" });
        Assert.Equal(5, confirmedByPerson.Level);
    }

    [Fact]
    public void Development_components_stay_at_2_unless_a_floor_applies()
    {
        var quiet = RuleEngine.Evaluate(Rules, Base with { DevelopmentOnly = true, CvssScore = 9.8, Exploitation = "poc", Exposure = "internet" });
        Assert.Equal(2, quiet.Level);
        Assert.Equal([new BoundRow("development", 2)], quiet.Caps);

        // An actively exploited tool on the build server is a real risk (master plan 16.5).
        var exploited = RuleEngine.Evaluate(Rules, Base with { DevelopmentOnly = true, CvssScore = 9.8, Exploitation = "active", Exposure = "internal" });
        Assert.Equal(5, exploited.Level);
        Assert.Contains(new BoundRow("active_in_range_severe", 5), exploited.Floors);
    }

    [Fact]
    public void Floors_for_active_exploitation_hold_against_every_other_factor()
    {
        // Low severity, isolated, low criticality, protected: points say 2.
        var low = Base with { CvssScore = 2.0, Exposure = "isolated", Criticality = "low", Protection = "effective", Exploitation = "active" };

        Assert.Equal(4, RuleEngine.Evaluate(Rules, low).Level);
        Assert.Equal(3, RuleEngine.Evaluate(Rules, low with { VersionVerdict = "unknown" }).Level);
        Assert.Equal(4, RuleEngine.Evaluate(Rules, low with { CvssScore = 9.0 }).Level);
        Assert.Equal(5, RuleEngine.Evaluate(Rules, low with { CvssScore = 9.0, Exposure = "internal" }).Level);
    }

    [Fact]
    public void The_model_lowers_by_at_most_one_level_and_cannot_exclude()
    {
        var input = Base with { VersionVerdict = "unknown", CvssScore = 7.5, Exploitation = "poc" };
        var withoutModel = RuleEngine.Evaluate(Rules, input);
        var unlikely = RuleEngine.Evaluate(Rules, input with { ModelVerdict = "not_affected" });

        Assert.Equal(4, withoutModel.Level);
        Assert.Equal(3, unlikely.Level);
        Assert.Contains(new BoundRow("model_max_drop", 3), unlikely.Floors);
        Assert.Null(unlikely.Exclusion);
    }

    [Fact]
    public void The_same_stored_input_gives_the_same_level_and_factors()
    {
        var input = Base with
        {
            CvssScore = 9.4,
            Exploitation = "active",
            ExploitationSource = "cisa-kev",
            ExploitationSince = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero),
            Criticality = "high",
            EpssClass = "low",
            AuthorityWarning = true,
        };

        var first = JsonSerializer.Serialize(RuleEngine.Evaluate(Rules, input), AlertAssessor.Json);
        var second = JsonSerializer.Serialize(RuleEngine.Evaluate(RuleSet.Load(MasterDataOptions.FindDefault()), input), AlertAssessor.Json);

        Assert.Equal(first, second);
        Assert.Contains("\"evidence\":\"cisa-kev since 2026-11-17\"", first, StringComparison.Ordinal);
        Assert.Contains("\"rulesetVersion\":1", first, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rule_set_names_no_deadlines_and_an_old_file_with_them_still_loads()
    {
        // Reminders have no deadline (master plan 17.5, ADR 046). A data folder of the previous version still names them:
        // for one version they are read and ignored, and the levels stay the same.
        var text = File.ReadAllText(Path.Combine(MasterDataOptions.FindDefault(), RuleSet.Folder, "ruleset-1.json"));
        Assert.DoesNotContain("deadline", text, StringComparison.OrdinalIgnoreCase);
        var withDeadlines = RuleSet.Parse(text.TrimEnd().TrimEnd('}') + ", \"deadlineHours\": { \"5\": 48, \"4\": 168, \"3\": 720, \"2\": 2160 } }");
        Assert.Equal(Rules.Levels, withDeadlines.Levels);
        Assert.Equal(Rules.Version, withDeadlines.Version);
    }

    [Theory]
    [InlineData(null, "unrated")]
    [InlineData(0.0, "low")]
    [InlineData(3.9, "low")]
    [InlineData(4.0, "medium")]
    [InlineData(6.9, "medium")]
    [InlineData(7.0, "high")]
    [InlineData(8.9, "high")]
    [InlineData(9.0, "critical")]
    [InlineData(10.0, "critical")]
    public void Severity_classes_follow_cvss(double? score, string severity) => Assert.Equal(severity, Rules.SeverityOf(score));

    /// <summary>Every value of every factor the rule set file names, with the input that selects it.</summary>
    public static TheoryData<string, string> FactorValues
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (factor, values) in Rules.Points)
            {
                foreach (var value in values.Keys)
                {
                    data.Add(factor, value);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FactorValues))]
    public void Every_value_of_every_factor_in_the_rule_set_file_counts_with_its_points(string factor, string value)
    {
        var input = factor switch
        {
            "severity" => Base with { CvssScore = value switch { "critical" => 9.5, "high" => 7.5, "medium" => 5.0, "low" => 2.0, _ => null } },
            "exploitation" => Base with { Exploitation = value },
            "affectedness" => value switch
            {
                "confirmed" => Base,
                "unknown" => Base with { VersionVerdict = "unknown" },
                _ => Base with { VersionVerdict = "unknown", HumanVerdict = value },
            },
            "criticality" => Base with { Criticality = value },
            "exposure" => Base with { Exposure = value },
            "protection" => Base with { Protection = value },
            "authorityWarning" => Base with { AuthorityWarning = value == "yes" },
            "epss" => Base with { EpssClass = value },
            _ => throw new InvalidOperationException($"No input selects {factor}; the engine must learn the factor before the file names it."),
        };

        var result = RuleEngine.Evaluate(Rules, input);

        var row = Assert.Single(result.Factors, f => f.Factor == factor);
        Assert.Equal((value, Rules.PointsOf(factor, value)), (row.Value, row.Points));
        Assert.Equal(result.Factors.Sum(f => f.Points), result.Points);
        Assert.Equal(Rules.LevelOf(result.Points!.Value), result.PointsLevel);
    }

    [Theory]
    [InlineData(20, 5)]
    [InlineData(8, 5)]
    [InlineData(7, 4)]
    [InlineData(5, 4)]
    [InlineData(4, 3)]
    [InlineData(3, 2)]
    [InlineData(-6, 2)]
    public void The_sum_gives_the_level_of_the_table_at_its_edges(int points, int level) => Assert.Equal(level, Rules.LevelOf(points));

    [Fact]
    public void A_missing_epss_or_an_unknown_exploitation_counts_as_no_signal()
    {
        var result = RuleEngine.Evaluate(Rules, Base with { EpssClass = null, Exploitation = "unknown", Protection = "partial" });

        Assert.Equal(
            ["exploitation none 0", "protection none 0", "epss low 0"],
            result.Factors.Where(f => f.Factor is "exploitation" or "protection" or "epss").Select(f => $"{f.Factor} {f.Value} {f.Points}"));
        Assert.Equal(RuleEngine.Evaluate(Rules, Base).Level, result.Level);
    }

    [Fact]
    public void An_unrated_entry_counts_as_medium_and_misses_the_floor_for_severe_flaws()
    {
        // Actively exploited in range on an internet system, but without a score: the floor of 5 asks for high or critical.
        var unrated = RuleEngine.Evaluate(Rules, Base with { CvssScore = null, Exploitation = "active", Exposure = "internet" });

        Assert.Equal(("severity", "unrated", "no score", 2), (unrated.Factors[0].Factor, unrated.Factors[0].Value, unrated.Factors[0].Evidence, unrated.Factors[0].Points));
        Assert.Equal(4, unrated.Level);
        Assert.DoesNotContain(unrated.Floors, f => f.Bound == "active_in_range_severe");

        Assert.Equal(5, RuleEngine.Evaluate(Rules, Base with { CvssScore = 7.0, Exploitation = "active", Exposure = "internet", Criticality = "low", Protection = "effective" }).Level);
    }

    [Fact]
    public void Each_exclusion_names_its_reason_and_the_order_is_fixed()
    {
        var all = Base with { StrongMatch = false, EntryWithdrawn = true, HumanExcluded = true, VersionVerdict = "out_of_range" };

        Assert.Equal("weak_match", RuleEngine.Evaluate(Rules, all).Exclusion);
        Assert.Equal("withdrawn", RuleEngine.Evaluate(Rules, all with { StrongMatch = true }).Exclusion);
        Assert.Equal("human", RuleEngine.Evaluate(Rules, all with { StrongMatch = true, EntryWithdrawn = false }).Exclusion);
        var outside = RuleEngine.Evaluate(Rules, all with { StrongMatch = true, EntryWithdrawn = false, HumanExcluded = false });
        Assert.Equal(("out_of_range", 1, (int?)null, (int?)null), (outside.Exclusion, outside.Level, outside.Points, outside.LevelWithoutModel));
        Assert.Empty(outside.Factors);
    }

    [Fact]
    public void The_model_alone_never_gives_level_1()
    {
        // Everything speaks against the flaw and the model says «not affected»: the level stays at the lowest of the points (7.5).
        var result = RuleEngine.Evaluate(Rules, Base with
        {
            VersionVerdict = "unknown",
            CvssScore = 2.0,
            Criticality = "low",
            Exposure = "isolated",
            Protection = "effective",
            ModelVerdict = "not_affected",
        });

        Assert.Null(result.Exclusion);
        Assert.Equal(2, result.Level);
        Assert.Equal(("affectedness", "unlikely"), (result.Factors[2].Factor, result.Factors[2].Value));
    }

    [Fact]
    public void Affected_from_the_model_does_not_lift_the_cap_that_reserves_critical_for_confirmed_affectedness()
    {
        // 9.8, a public exploit, a critical system on the internet: 8 points with the version unknown, 9 with the model's «affected».
        var input = Base with { VersionVerdict = "unknown", CvssScore = 9.8, Exploitation = "poc", Criticality = "high", Exposure = "internet" };

        var model = RuleEngine.Evaluate(Rules, input with { ModelVerdict = "affected" });
        Assert.Equal((9, 4), (model.Points, model.Level));
        Assert.Equal([new BoundRow("unconfirmed", 4)], model.Caps);
        Assert.Equal(5, RuleEngine.Evaluate(Rules, input with { HumanVerdict = "likely" }).Level);
    }

    [Fact]
    public void A_persons_verdict_is_not_held_by_the_floor_of_the_models_verdict()
    {
        // ADR 039: a person's «unlikely» counts in full; the same verdict of the model lowers by one level at most.
        var input = Base with { VersionVerdict = "unknown", CvssScore = 9.0, Criticality = "high" };
        Assert.Equal((5, 4), (RuleEngine.Evaluate(Rules, input).Points, RuleEngine.Evaluate(Rules, input).Level));

        var person = RuleEngine.Evaluate(Rules, input with { HumanVerdict = "unlikely" });
        Assert.Equal((3, 2), (person.Points, person.Level));
        Assert.Null(person.LevelWithoutModel);
        Assert.Empty(person.Floors);

        var model = RuleEngine.Evaluate(Rules, input with { ModelVerdict = "unlikely" });
        Assert.Equal((3, 3, 4), (model.Points, model.Level, model.LevelWithoutModel));
        Assert.Equal([new BoundRow("model_max_drop", 3)], model.Floors);
    }

    [Fact]
    public void The_models_verdict_does_not_undercut_the_floor_for_active_exploitation()
    {
        var input = Base with { VersionVerdict = "unknown", CvssScore = 2.0, Exploitation = "active", Exposure = "isolated", Criticality = "low" };

        var result = RuleEngine.Evaluate(Rules, input with { ModelVerdict = "not_affected" });

        Assert.Equal((0, 3, 3), (result.Points, result.Level, result.LevelWithoutModel));
        Assert.Equal([new BoundRow("active", 3)], result.Floors);
    }

    [Fact]
    public void Across_every_combination_the_bounds_hold_and_the_model_lowers_by_at_most_one_level()
    {
        // Master plan 7.6 steps 3 and 4 as properties: an upper bound is never exceeded without active exploitation, a lower
        // bound is never undercut, the model moves the level by at most one below the result without it and excludes nothing.
        double?[] scores = [null, 2.0, 5.0, 7.5, 9.0, 10.0];
        (string? Human, string? Model)[] verdicts =
            [(null, null), ("affected", null), ("likely", null), ("unlikely", null), (null, "affected"), (null, "likely"), (null, "uncertain"), (null, "unlikely"), (null, "not_affected")];
        var inputs =
            from score in scores
            from exploitation in new[] { "none", "poc", "active" }
            from version in new[] { "in_range", "unknown" }
            from criticality in new[] { "high", "normal", "low" }
            from exposure in new[] { "internet", "internal", "isolated" }
            from protection in new[] { "none", "effective" }
            from epss in new[] { null, "low", "high" }
            from development in new[] { false, true }
            from verdict in verdicts
            select Base with
            {
                CvssScore = score,
                Exploitation = exploitation,
                VersionVerdict = version,
                Criticality = criticality,
                Exposure = exposure,
                Protection = protection,
                EpssClass = epss,
                DevelopmentOnly = development,
                HumanVerdict = verdict.Human,
                ModelVerdict = verdict.Model,
            };

        var broken = new List<string>();
        var count = 0;
        foreach (var input in inputs)
        {
            count++;
            var result = RuleEngine.Evaluate(Rules, input);
            var level = result.Level;
            var confirmed = input.VersionVerdict == "in_range" || input.HumanVerdict is "affected" or "likely";
            var active = input.Exploitation == "active";
            var severe = Rules.SeverityOf(input.CvssScore) is "high" or "critical" && input.Exposure != "isolated";
            var withoutModel = input.ModelVerdict is null ? null : RuleEngine.Evaluate(Rules, input with { ModelVerdict = null });
            string?[] problems =
            [
                result.Exclusion is null && level is >= 2 and <= 5 ? null : "level outside 2 to 5 or excluded",
                active || confirmed || level <= Rules.Caps.Unconfirmed ? null : "cap for unconfirmed exceeded",
                active || !input.DevelopmentOnly || level <= Rules.Caps.Development ? null : "cap for development exceeded",
                !active || level >= Rules.Floors.Active ? null : "floor for active undercut",
                !active || input.VersionVerdict != "in_range" || level >= Rules.Floors.ActiveInRange ? null : "floor for active in range undercut",
                !active || input.VersionVerdict != "in_range" || !severe || level >= Rules.Floors.ActiveInRangeSevere ? null : "floor for severe undercut",
                withoutModel is null
                    ? result.LevelWithoutModel is null ? null : "a level without the model although none counted"
                    : result.LevelWithoutModel != withoutModel.Level ? $"level without the model {result.LevelWithoutModel}, the rules alone give {withoutModel.Level}"
                    : level < withoutModel.Level - Rules.Floors.ModelMaxDrop ? "the model lowered by more than one level"
                    : null,
                input.ModelVerdict != "not_affected" || level == RuleEngine.Evaluate(Rules, input with { ModelVerdict = "unlikely" }).Level ? null : "the model's not_affected counts other than unlikely",
            ];
            broken.AddRange(problems.OfType<string>().Select(p => $"{p}: {input}"));
        }

        Assert.Equal(34_992, count);
        Assert.Empty(broken.Take(5));
    }

    [Fact]
    public void The_explanation_names_the_evidence_of_each_factor()
    {
        static string? Affectedness(RuleInput input) => RuleEngine.Evaluate(Rules, input).Factors.Single(f => f.Factor == "affectedness").Evidence;

        var unknown = Base with { VersionVerdict = "unknown", InstalledVersion = null };
        Assert.Equal("version not recorded, affectedness unknown (no comparable range)", Affectedness(unknown));
        Assert.Equal("version 1.0, affectedness unknown (range_not_readable); model: likely", Affectedness(unknown with { InstalledVersion = "1.0", VersionReason = "range_not_readable", ModelVerdict = "likely" }));
        Assert.Equal("version not recorded, affectedness unknown (no comparable range); person: unlikely", Affectedness(unknown with { HumanVerdict = "unlikely", ModelVerdict = "likely" }));

        var exploited = RuleEngine.Evaluate(Rules, Base with { Exploitation = "active", ExploitationSource = "ssvc", CvssVersion = null });
        Assert.Equal("ssvc", exploited.Factors[1].Evidence);
        Assert.Equal("CVSS ?: 7.5", exploited.Factors[0].Evidence);
        Assert.Null(RuleEngine.Evaluate(Rules, Base with { Exploitation = "poc" }).Factors[1].Evidence);
    }

    [Fact]
    public void A_broken_rule_set_is_refused_with_its_problems()
    {
        var json = File.ReadAllText(Path.Combine(MasterDataOptions.FindDefault(), "rules", "ruleset-1.json"))
            .Replace("\"poc\": 2, ", string.Empty, StringComparison.Ordinal)
            .Replace("{ \"minPoints\": 4, \"level\": 3 }", "{ \"minPoints\": 9, \"level\": 3 }", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => RuleSet.Parse(json));

        Assert.Contains("points.exploitation.poc is missing", error.Message, StringComparison.Ordinal);
        Assert.Contains("levels must go down", error.Message, StringComparison.Ordinal);
    }
}
