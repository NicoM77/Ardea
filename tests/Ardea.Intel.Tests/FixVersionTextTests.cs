using Ardea.Intel.Correlation;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Tests;

/// <summary>The fix versions an entry's text names (master plan 6.6, ADR 033), from sentences of real records.</summary>
public sealed class FixVersionTextTests
{
    [Theory]
    // Apache (CVE-2026-122096): the range says «through 9.0.0», the text names the fix.
    [InlineData("This issue affects Apache DataSketches Java: through 9.0.0.\n\nUsers are recommended to upgrade to version 9.1.0, which fixes the issue.", "9.1.0")]
    [InlineData("This issue affects Apache XML Graphics Batik: through 1.19.\n\nUsers are recommended to upgrade to version 1.20, which fixes the issue.", "1.20")]
    [InlineData("Users are recommended to upgrade to version 10.3.0 or 11.0.0-M2, which fix the issue.", "10.3.0|11.0.0-M2")]
    [InlineData("Users are recommended to upgrade to version 6.4.0, 6.3.3 or 5.19.12, which fixes the issue.", "6.4.0|6.3.3|5.19.12")]
    // Chrome, VulnCheck: «prior to», «before», with a list of lines.
    [InlineData("Type confusion in V8 in Google Chrome prior to 157.0.8103.50 allowed a remote attacker to execute arbitrary code.", "157.0.8103.50")]
    [InlineData("n8n before 1.123.89, 2.x before 2.45.5, 2.46.x before 2.46.1, and 3.x before 3.0.1 contains an open redirect.", "1.123.89|2.45.5|2.46.1|3.0.1")]
    // Fortinet's solutions name the product between «upgrade to» and the version.
    [InlineData("Upgrade to FortiManager version 8.0.2 or above\nUpgrade to FortiManager Cloud version 7.6.8 or above", "8.0.2|7.6.8")]
    // Mattermost names the product between «update» and «to».
    [InlineData("Update Mattermost Desktop App to versions 6.4.0, 6.3.1.0, 6.2.4.0 or higher.", "6.4.0|6.3.1.0|6.2.4.0")]
    [InlineData("The issue was fixed in 2.5.3 and patched in v3.1.0-rc.2.", "2.5.3|3.1.0-rc.2")]
    public void Versions_named_as_fixes_are_found(string text, string expected) =>
        Assert.Equal(expected.Split('|'), FixVersionText.Extract([text]));

    [Theory]
    // The last affected version is no fix; in multi-product descriptions it is often another product's.
    [InlineData("A vulnerability in Fortinet FortiManager 8.0.0, FortiManager 7.6.0 through 7.6.7, FortiManager Cloud 8.0.0 through 8.0.1 may allow an administrator to escalate privileges.")]
    [InlineData("This issue affects Apache log4net: from 1.2.9.")]
    // Years, counts and plain numbers are no versions.
    [InlineData("Accounts created before 2024 and retried prior to 3 attempts are affected before authentication.")]
    [InlineData("Version 2.4.5 is affected. CVSS 9.8.")]
    public void Other_numbers_are_not_fixes(string text) => Assert.Empty(FixVersionText.Extract([text]));

    [Fact]
    public void Description_and_solutions_count_each_version_once()
    {
        Assert.Equal(["9.1.0"], FixVersionText.Extract(["Upgrade to version 9.1.0.", null, "Fixed in 9.1.0."]));
    }

    [Fact]
    public void The_projection_reads_them_from_description_and_solutions_and_they_are_material()
    {
        ClaimBlock Block(string solution) => new()
        {
            Aliases = [new Identifier("CVE", "CVE-2026-122096")],
            Description = "This issue affects Apache DataSketches Java: through 9.0.0.",
            Remediation = new Remediation { FixAvailable = true, Solutions = [solution] },
        };
        Contribution Of(ClaimBlock block) =>
            new(Guid.Empty, "cve-program", "mandated", Guid.Empty, null, new Observation { Kind = ObservationKind.Vulnerability, Claims = [block] }, [block]);

        var entry = Projection.Compute([Of(Block("Users are recommended to upgrade to version 9.1.0, which fixes the issue."))]);
        var other = Projection.Compute([Of(Block("Users are recommended to upgrade to version 9.2.0, which fixes the issue."))]);

        Assert.Equal(["9.1.0"], entry.FixVersionsInText);
        Assert.NotEqual(entry.MaterialFingerprint(null), other.MaterialFingerprint(null));
    }
}
