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
    [InlineData(7.0, "high")]
    [InlineData(9.0, "critical")]
    public void Severity_classes_follow_cvss(double? score, string severity) => Assert.Equal(severity, Rules.SeverityOf(score));

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
