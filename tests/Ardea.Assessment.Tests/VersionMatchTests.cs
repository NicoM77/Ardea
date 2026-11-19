using Ardea.Assessment.Matching;
using Ardea.Assets.Contracts;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assessment.Tests;

/// <summary>The version check of a strong hit over all statements of an entry (master plan 6.6; ADR 033), without a database.</summary>
public sealed class VersionMatchTests
{
    private static readonly IVersionSchemes Schemes = ResolveSchemes();

    private static readonly Guid CveProgram = Guid.CreateVersion7();

    private static readonly Guid Osv = Guid.CreateVersion7();

    [Fact]
    public void The_scheme_must_read_every_bound_when_one_can()
    {
        // CVE-2026-110941 (Mattermost): two ranges typed semver, the unaffected versions untyped and with four parts.
        // Semver reads the installed 6.3.3 but not 6.3.1.0, which left the statement «range not readable».
        AffectedFacts[] affected =
        [
            Row(lastAffected: "6.0.3", scheme: "semver"),
            Row(lastAffected: "6.3.2", scheme: "semver"),
            Row(exact: "6.4.0", status: "unaffected"),
            Row(exact: "6.3.1.0", status: "unaffected"),
            Row(exact: "6.2.4.0", status: "unaffected"),
        ];

        var result = VersionMatch.Check(Component("6.3.3", "semver"), MatchTypes.Product, affected, Schemes);

        Assert.Equal(("out_of_range", "default_unaffected"), (result.Verdict, result.Reason));
        Assert.Equal("numeric", Assert.Single(result.Statements).Scheme);
    }

    [Fact]
    public void The_record_scheme_stays_when_it_reads_every_bound()
    {
        AffectedFacts[] affected = [Row(introduced: "1.0.0-rc.1", @fixed: "1.0.0", scheme: "semver")];

        var result = VersionMatch.Check(Component("1.0.0-rc.2", null), MatchTypes.Product, affected, Schemes);

        Assert.Equal(("in_range", "semver"), (result.Verdict, Assert.Single(result.Statements).Scheme));
    }

    [Fact]
    public void A_fix_named_in_the_entrys_text_keeps_a_version_above_the_ranges_unknown()
    {
        AffectedFacts[] affected = [Row(lastAffected: "9.0.0", scheme: "semver")];

        var withText = VersionMatch.Check(Component("9.0.1", null), MatchTypes.Product, affected, Schemes, ["9.1.0"]);
        var without = VersionMatch.Check(Component("9.0.1", null), MatchTypes.Product, affected, Schemes);

        Assert.Equal(("unknown", "fix_named_in_text_above_installed"), (withText.Verdict, withText.Reason));
        Assert.Equal(("out_of_range", "default_unaffected"), (without.Verdict, without.Reason));
    }

    [Fact]
    public void An_empty_range_of_another_line_does_not_decide_when_the_versions_own_line_is_fixed()
    {
        // CVE-2026-114147 (reference pair P055): «Visual Studio 2026 version 18.11» from 18.11.0 before 18.11.4, and
        // «version 18.12» as 18.12.0 to before 18.12.0. Read alone, the empty range would put 18.11.9 in range.
        AffectedFacts[] affected =
        [
            Row(introduced: "18.11.0", @fixed: "18.11.4", defaultStatus: "unknown", product: "Microsoft Visual Studio 2026 version 18.11"),
            Row(introduced: "18.12.0", @fixed: "18.12.0", defaultStatus: "unknown", product: "Microsoft Visual Studio 2026 version 18.12"),
        ];

        var result = VersionMatch.Check(Component("18.11.9", null), MatchTypes.Product, affected, Schemes);

        Assert.Equal(("out_of_range", "above_all_ranges"), (result.Verdict, result.Reason));
        Assert.Equal(2, result.Statements.Count);
    }

    [Fact]
    public void An_empty_range_alone_reads_as_before_its_fix_and_names_the_fix()
    {
        // Google: «Chrome prior to 157.0.8103.50», no other statement about the installed version's line.
        AffectedFacts[] affected = [Row(introduced: "157.0.8103.50", @fixed: "157.0.8103.50", defaultStatus: "unknown", product: "Chrome")];

        var result = VersionMatch.Check(Component("157.0.8102.50", null), MatchTypes.Product, affected, Schemes);

        Assert.Equal(("in_range", "before_empty_range", "157.0.8103.50"), (result.Verdict, result.Reason, result.FixedVersion));
    }

    [Fact]
    public void A_record_without_a_comparable_range_neither_decides_nor_vetoes()
    {
        // A source that names only Git commits says nothing about the installed version.
        var commits = Row(introduced: "1da177e4c3f4", @fixed: "a1b2c3d4e5f6", scheme: "git", defaultStatus: "unknown", source: Osv);

        var alone = VersionMatch.Check(Component("6.3.4", null), MatchTypes.Product, [commits], Schemes);
        var withRanges = VersionMatch.Check(Component("6.3.4", null), MatchTypes.Product, [commits, Row(lastAffected: "6.3.2")], Schemes);

        Assert.Equal(("unknown", "no_comparable_range"), (alone.Verdict, alone.Reason));
        Assert.Equal(("out_of_range", "default_unaffected"), (withRanges.Verdict, withRanges.Reason));
        Assert.Equal(2, withRanges.Statements.Count);
    }

    private static AffectedFacts Row(
        string? introduced = null, string? @fixed = null, string? lastAffected = null, string? exact = null,
        string status = "affected", string? scheme = null, string defaultStatus = "unaffected", string product = "Mattermost", Guid? source = null) => new()
        {
            SourceId = source ?? CveProgram,
            Vendor = "Mattermost",
            Product = product,
            ProductKey = "mattermost",
            VersionScheme = scheme,
            Introduced = introduced,
            Fixed = @fixed,
            LastAffected = lastAffected,
            Exact = exact,
            Status = status,
            DefaultStatus = defaultStatus,
        };

    private static ProfileComponent Component(string version, string? scheme) => new()
    {
        Id = Guid.CreateVersion7(),
        ProjectId = Guid.CreateVersion7(),
        ProjectArchived = false,
        ProjectName = "Chat",
        ProductKey = "mattermost",
        Name = "Mattermost Server",
        Kind = "other",
        Version = version,
        VersionScheme = scheme,
        Packaging = "upstream",
        Exposure = "internet",
        Criticality = "normal",
        Protection = "none",
        Origin = "manual",
    };

    private static IVersionSchemes ResolveSchemes()
    {
        var services = new ServiceCollection();
        new Catalog.CatalogModule().AddServices(services, new ConfigurationBuilder().Build());
        return services.BuildServiceProvider().GetRequiredService<IVersionSchemes>();
    }
}
