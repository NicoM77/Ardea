using Ardea.Intel.Correlation;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Tests;

public sealed class ProjectionTests
{
    private static readonly Guid Cve = Guid.CreateVersion7();
    private static readonly Guid Ncsc = Guid.CreateVersion7();
    private static readonly Guid Vendor = Guid.CreateVersion7();

    [Fact]
    public void Issuer_text_and_cna_severity_win_and_the_national_advisory_sets_the_mark()
    {
        var record = Contribution(Cve, "cve-program", "mandated", ObservationKind.Vulnerability, new ClaimBlock
        {
            Aliases = [new Identifier("CVE", "CVE-2026-1")],
            Title = "Issuer title",
            Description = "Issuer text",
            Metrics = [new SeverityMetric("CVSS", "3.1", "AV:N", 9.8) { Assigner = "acme" }, new SeverityMetric("CVSS", "3.1", "AV:L", 5.0) { Assigner = "CISA-ADP" }],
            Exploitation = new Exploitation(ExploitationState.ProofOfConcept),
        });
        var advisory = Contribution(Ncsc, "ncsc-hub", "authority", ObservationKind.Advisory, new ClaimBlock
        {
            Aliases = [new Identifier("CVE", "CVE-2026-1")],
            Title = "Advisory title",
            Exploitation = new Exploitation(ExploitationState.Active) { Evidence = "NCSC" },
        }, summary: "Short NCSC summary");

        var entry = Projection.Compute([advisory, record]);

        Assert.Equal("Issuer title", entry.Title);
        Assert.Equal("Issuer text", entry.Description);
        Assert.Equal("Short NCSC summary", entry.Summary);
        Assert.Equal(9.8, entry.CvssScore);
        Assert.Equal("cve-program/acme", entry.Provenance["severity"]);
        Assert.Equal("active", entry.Exploitation);
        Assert.True(entry.AuthorityWarning);
        Assert.Equal("ncsc-hub", entry.Provenance["authorityWarning"]);
        Assert.Equal(["describes", "mentions"], entry.Sources.Select(s => s.Relation).Order());
    }

    [Fact]
    public void Without_cna_severity_the_cisa_enrichment_counts()
    {
        var record = Contribution(Cve, "cve-program", "mandated", ObservationKind.Vulnerability, new ClaimBlock
        {
            Aliases = [new Identifier("CVE", "CVE-2026-2")],
            Metrics = [new SeverityMetric("CVSS", "3.1", "AV:N/AC:L", 7.5) { Assigner = "CISA-ADP" }],
        });

        Assert.Equal(7.5, Projection.Compute([record]).CvssScore);
    }

    [Fact]
    public void Missing_title_is_built_from_product_and_weakness()
    {
        var record = Contribution(Cve, "cve-program", "mandated", ObservationKind.Vulnerability, new ClaimBlock
        {
            Aliases = [new Identifier("CVE", "CVE-2026-3")],
            Weaknesses = ["CWE-79"],
            Affected = [new AffectedProduct { Vendor = "Acme", Product = "Portal" }],
        });

        var entry = Projection.Compute([record]);

        Assert.Equal("CWE-79 in Acme Portal", entry.Title);
        Assert.Equal("derived", entry.Provenance["title"]);
    }

    [Fact]
    public void A_cve_id_ranks_above_a_ghsa_id_as_primary_key()
    {
        var osv = Contribution(Vendor, "osv", "recognized", ObservationKind.Vulnerability, new ClaimBlock
        {
            Aliases = [new Identifier("GHSA", "GHSA-aaaa-bbbb-cccc"), new Identifier("CVE", "CVE-2026-4")],
        });

        Assert.Equal("CVE-2026-4", Projection.Compute([osv]).PrimaryKey);
    }

    [Fact]
    public void Withdrawal_by_the_issuer_overrules_everyone_else()
    {
        var record = Contribution(Cve, "cve-program", "mandated", ObservationKind.Vulnerability, new ClaimBlock { Aliases = [new Identifier("CVE", "CVE-2026-5")], State = VulnerabilityState.Rejected });
        var advisory = Contribution(Ncsc, "ncsc-hub", "authority", ObservationKind.Advisory, new ClaimBlock { Aliases = [new Identifier("CVE", "CVE-2026-5")] });

        Assert.Equal("rejected", Projection.Compute([record, advisory]).State);
    }

    [Fact]
    public void Text_changes_leave_the_material_fingerprint_alone_but_a_new_range_does_not()
    {
        var baseline = Block("old text", fixedIn: "1.2.3");
        var reworded = Block("new text", fixedIn: "1.2.3");
        var moved = Block("old text", fixedIn: "1.2.4");

        var a = Projection.Compute([Contribution(Cve, "cve-program", "mandated", ObservationKind.Vulnerability, baseline)]);
        var b = Projection.Compute([Contribution(Cve, "cve-program", "mandated", ObservationKind.Vulnerability, reworded)]);
        var c = Projection.Compute([Contribution(Cve, "cve-program", "mandated", ObservationKind.Vulnerability, moved)]);

        Assert.Equal(a.MaterialFingerprint(null), b.MaterialFingerprint(null));
        Assert.NotEqual(a.MaterialFingerprint(null), c.MaterialFingerprint(null));
        Assert.NotEqual(a.MaterialFingerprint(null), a.MaterialFingerprint("high"));
    }

    [Fact]
    public void Affected_products_carry_canonical_cpe_package_and_raw_key()
    {
        var record = Contribution(Cve, "cve-program", "mandated", ObservationKind.Vulnerability, new ClaimBlock
        {
            Aliases = [new Identifier("CVE", "CVE-2026-9")],
            Affected =
            [
                new AffectedProduct { Vendor = "The Apache Software Foundation", Product = "Log4j", Cpes = ["cpe:2.3:a:apache:log4j:2.14.1:*:*:*:*:*:*:*"] },
                new AffectedProduct { Purl = "pkg:npm/%40angular/core" },
            ],
        });

        var rows = Projection.Compute([record]).Affected;

        Assert.Contains(rows, r => r.CpeVendor == "apache" && r.CpeProduct == "log4j" && r.ProductKey == "apache/log4j");
        Assert.Contains(rows, r => r.PurlType == "npm" && r.PurlName == "@angular/core");
    }

    [Fact]
    public void Affected_ranges_are_stored_with_their_peculiarities_resolved()
    {
        var record = Contribution(Cve, "cve-program", "mandated", ObservationKind.Vulnerability, new ClaimBlock
        {
            Aliases = [new Identifier("CVE", "CVE-2026-114949")],
            Affected =
            [
                new AffectedProduct { Vendor = "Oracle Corporation", Product = "MySQL Server", Versions = [new VersionRange { Exact = "8.4.0-8.4.13" }] },
                new AffectedProduct
                {
                    Vendor = "Linux",
                    Product = "Linux",
                    VersionScheme = "semver",
                    Versions = [new VersionRange { Introduced = "6.12.116", LastAffected = "6.12.*", Status = AffectedStatus.Unaffected }],
                },
            ],
        });

        var rows = Projection.Compute([record]).Affected;

        Assert.Contains(rows, r => r.Product == "MySQL Server" && r is { Introduced: "8.4.0", LastAffected: "8.4.13", Exact: null, Status: "affected" });
        Assert.Contains(rows, r => r.Product == "Linux" && r is { Introduced: "6.12.116", Fixed: "6.13", LastAffected: null, Status: "unaffected", VersionScheme: "semver" });
    }

    [Fact]
    public void A_version_type_longer_than_the_column_is_dropped_so_the_range_falls_back_to_the_unknown_scheme()
    {
        var malformed = "customDKCMAIN Ver.93-" + new string('x', 80);
        var record = Contribution(Cve, "cve-program", "mandated", ObservationKind.Vulnerability, new ClaimBlock
        {
            Aliases = [new Identifier("CVE", "CVE-2025-7737")],
            Affected =
            [
                new AffectedProduct
                {
                    Vendor = new string('v', 600),
                    Product = "Portal",
                    VersionScheme = malformed,
                    Versions = [new VersionRange { Introduced = "1.0", Fixed = "2.0", Scheme = malformed }],
                },
            ],
        });

        var row = Assert.Single(Projection.Compute([record]).Affected);

        Assert.Null(row.VersionScheme);
        Assert.Equal("1.0", row.Introduced);
        Assert.Equal("2.0", row.Fixed);
        Assert.Equal(500, row.Vendor!.Length);
    }

    private static ClaimBlock Block(string description, string fixedIn) => new()
    {
        Aliases = [new Identifier("CVE", "CVE-2026-6")],
        Description = description,
        Affected = [new AffectedProduct { Vendor = "acme", Product = "lib", Versions = [new VersionRange { Introduced = "1.0", Fixed = fixedIn }] }],
    };

    private static Contribution Contribution(Guid source, string key, string operatorKind, ObservationKind kind, ClaimBlock block, string? summary = null) =>
        new(source, key, operatorKind, Guid.CreateVersion7(), null, new Observation { Kind = kind, Summary = summary, Claims = [block] }, [block]);
}
