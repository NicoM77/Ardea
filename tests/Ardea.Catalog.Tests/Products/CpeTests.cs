using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Tests.Products;

public sealed class CpeTests
{
    [Fact]
    public void Formatted_string_is_read_attribute_by_attribute()
    {
        var cpe = Cpe.Parse("cpe:2.3:a:microsoft:asp.net_core:8.0.0:*:*:*:*:*:x64:*");

        Assert.Equal(("a", "microsoft", "asp.net_core", "8.0.0"), (cpe.Part, cpe.Vendor, cpe.Product, cpe.Version));
        Assert.Equal((Cpe.Any, "x64"), (cpe.Update, cpe.TargetHardware));
        Assert.Equal(new CpePair("microsoft", "asp.net_core"), cpe.Pair);
    }

    [Theory]
    [InlineData(@"cpe:2.3:a:gnu:c\+\+:4.0:*:*:*:*:*:*:*", @"c\+\+")]
    [InlineData(@"cpe:2.3:a:nodejs:node\.js:*:*:*:*:*:*:*:*", "node.js")]
    [InlineData(@"cpe:2.3:a:acme:prod\:uct:1.0:*:*:*:*:*:*:*", @"prod\:uct")]
    [InlineData(@"cpe:2.3:a:acme:big\!deal:*:*:*:*:*:*:*:*", @"big\!deal")]
    [InlineData("cpe:2.3:a:Microsoft:Edge_Chromium:*:*:*:*:*:*:*:*", "edge_chromium")]
    public void Escapes_are_kept_canonically(string text, string product) => Assert.Equal(product, Cpe.Parse(text).Product);

    [Fact]
    public void Escaped_colon_does_not_split_attributes()
    {
        var cpe = Cpe.Parse(@"cpe:2.3:a:acme:prod\:uct:1.0");

        Assert.Equal(("1.0", Cpe.Any), (cpe.Version, cpe.Other));
    }

    [Fact]
    public void Uri_binding_reads_like_the_formatted_string()
    {
        Assert.Equal(
            Cpe.Parse("cpe:2.3:a:microsoft:asp.net_core:8.0.0:*:*:*:*:*:*:*"),
            Cpe.Parse("cpe:/a:microsoft:asp.net_core:8.0.0"));
    }

    [Fact]
    public void Uri_percent_encoding_and_wildcards_are_decoded()
    {
        Assert.Equal(@"c\+\+", Cpe.Parse("cpe:/a:gnu:c%2b%2b:4.0").Product);
        Assert.Equal("2.4.*", Cpe.Parse("cpe:/a:apache:http_server:2.4.%02").Version);
        Assert.Equal("2.4.?", Cpe.Parse("cpe:/a:apache:http_server:2.4.%01").Version);
    }

    [Fact]
    public void Uri_packed_edition_is_unpacked()
    {
        var cpe = Cpe.Parse("cpe:/a:hp:insight_diagnostics:7.4.0.1570:-:~~online~win2003~x64~");

        Assert.Equal(
            ("7.4.0.1570", Cpe.NotApplicable, Cpe.Any, "online", "win2003", "x64", Cpe.Any),
            (cpe.Version, cpe.Update, cpe.Edition, cpe.SoftwareEdition, cpe.TargetSoftware, cpe.TargetHardware, cpe.Other));
    }

    [Fact]
    public void Formatted_string_round_trips()
    {
        const string text = @"cpe:2.3:a:gnu:c\+\+:4.0:-:*:*:*:*:*:*";

        Assert.Equal(text, Cpe.Parse(text).ToString());
    }

    [Theory]
    [InlineData("cpe:2.3:x:acme:tool:1.0")]
    [InlineData("cpe:2.3:a:a:b:c:d:e:f:g:h:i:j:k")]
    [InlineData(@"cpe:2.3:a:acme:tool\")]
    [InlineData("cpe:2.3:a:acme::1.0")]
    [InlineData("cpe:/a:acme:tool:1.0:%zz")]
    [InlineData("acme:tool")]
    [InlineData("")]
    public void Malformed_names_are_rejected(string text) => Assert.False(Cpe.TryParse(text, out _));

    [Theory]
    [InlineData("cpe:2.3:a:apache:http_server:*:*:*:*:*:*:*:*", "cpe:2.3:a:apache:http_server:2.4.49:*:*:*:*:*:*:*", true)]
    [InlineData("cpe:2.3:a:apache:http_server:2.4.*:*:*:*:*:*:*:*", "cpe:2.3:a:apache:http_server:2.4.49:*:*:*:*:*:*:*", true)]
    [InlineData("cpe:2.3:a:apache:http_server:2.4.*:*:*:*:*:*:*:*", "cpe:2.3:a:apache:http_server:2.5.0:*:*:*:*:*:*:*", false)]
    [InlineData("cpe:2.3:a:apache:http_server:2.4.4?:*:*:*:*:*:*:*", "cpe:2.3:a:apache:http_server:2.4.49:*:*:*:*:*:*:*", true)]
    [InlineData("cpe:2.3:a:apache:tomcat:*:*:*:*:*:*:*:*", "cpe:2.3:a:apache:http_server:2.4.49:*:*:*:*:*:*:*", false)]
    [InlineData("cpe:2.3:o:microsoft:windows_10:-:*:*:*:*:*:*:*", "cpe:2.3:o:microsoft:windows_10:1809:*:*:*:*:*:*:*", false)]
    [InlineData("cpe:2.3:o:microsoft:windows_10:-:*:*:*:*:*:*:*", "cpe:2.3:o:microsoft:windows_10:-:*:*:*:*:*:*:*", true)]
    [InlineData("cpe:2.3:a:apache:http_server:2.4.49:*:*:*:*:*:*:*", "cpe:2.3:a:apache:http_server:*:*:*:*:*:*:*:*", true)]
    [InlineData(@"cpe:2.3:a:acme:c\*:*:*:*:*:*:*:*:*", "cpe:2.3:a:acme:cx:*:*:*:*:*:*:*:*", false)]
    public void Pattern_matches_per_attribute(string pattern, string target, bool matches) =>
        Assert.Equal(matches, Cpe.Parse(pattern).Matches(Cpe.Parse(target)));

    [Theory]
    [InlineData("microsoft:asp.net_core", "microsoft", "asp.net_core")]
    [InlineData(@"gnu:c\+\+", "gnu", @"c\+\+")]
    [InlineData("Oracle:MySQL", "oracle", "mysql")]
    public void Pair_is_read_from_vendor_and_product(string text, string vendor, string product)
    {
        Assert.True(CpePair.TryParse(text, out var pair));
        Assert.Equal(new CpePair(vendor, product), pair);
        Assert.Equal($"{vendor}:{product}", pair.ToString());
    }

    [Theory]
    [InlineData("microsoft")]
    [InlineData("microsoft:*")]
    [InlineData("-:product")]
    [InlineData("a:b:c")]
    [InlineData("acme:tool?")]
    public void Pair_needs_a_concrete_vendor_and_product(string text) => Assert.False(CpePair.TryParse(text, out _));
}
