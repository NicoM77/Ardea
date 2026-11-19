using Ardea.Catalog.Contracts;
using Ardea.Catalog.Learning;
using Ardea.Catalog.Products;
using Ardea.Catalog.Tests.Products;

namespace Ardea.Catalog.Tests.Learning;

/// <summary>The mapping without a model (master plan 6.9 step 2), over the curated catalogue of the repository.</summary>
public sealed class RuleMappingTests
{
    private static readonly CatalogLayers Catalog = new(CatalogFiles.Load(CatalogFilesTests.RepositoryCatalog).Products);

    private static readonly HashSet<string> Keys = [.. Catalog.All.Products.Select(p => p.Key)];

    [Theory]
    [InlineData("PostgreSQL Global Development Group", "PostgreSQL 17.2", "postgresql")]
    [InlineData("Fortinet, Inc.", "FortiOS version 7.4", "fortios")]
    [InlineData("HashiCorp", "HashiCorp Vault v1.15", "vault")]
    [InlineData("Microsoft Corporation", "Microsoft ASP.NET Core", "aspnetcore")]
    public void The_same_name_but_for_legal_form_version_vendor_and_spelling_is_an_alias(string vendor, string product, string key)
    {
        var proposal = RuleMapping.Map(Statement(vendor, product), Catalog, Keys);

        Assert.Equal((Decisions.Alias, key, LearnedBy.Rule, "high"), (proposal?.Decision, proposal?.AliasOf, proposal?.LearnedBy, proposal?.Confidence));
    }

    [Fact]
    public void A_cpe_pair_an_entry_knows_is_an_alias_of_it()
    {
        var proposal = RuleMapping.Map(Statement("Acme", "Postgres Fork Thing", cpe: ["postgresql:postgresql"]), Catalog, Keys);

        Assert.Equal((Decisions.Alias, "postgresql"), (proposal?.Decision, proposal?.AliasOf));
    }

    [Fact]
    public void An_unknown_cpe_pair_gives_a_new_entry_with_name_and_vendor_of_the_record_and_the_numeric_scheme()
    {
        var proposal = RuleMapping.Map(Statement("Acme Corp.", "Widget Server 2.4", cpe: ["acme:widget_server"]), Catalog, Keys);

        var product = proposal?.NewProduct;
        Assert.Equal(Decisions.New, proposal?.Decision);
        Assert.Equal(("widget-server", "Widget Server", "Acme Corp.", "other", "numeric"), (product?.Key, product?.Name, product?.Vendor, product?.Kind, product?.VersionScheme));
        Assert.Equal(["acme:widget_server"], product?.Cpe);
        Assert.Equal("Widget Server 2.4", Assert.Single(Assert.Single(product!.Aliases).Names));
    }

    [Fact]
    public void An_unknown_package_gives_a_new_library_with_the_ecosystems_scheme()
    {
        var proposal = RuleMapping.Map(Statement(null, "left-pad-ng", purl: "pkg:npm/left-pad-ng"), Catalog, Keys);

        Assert.Equal(("left-pad-ng", "library", "semver"), (proposal?.NewProduct?.Key, proposal?.NewProduct?.Kind, proposal?.NewProduct?.VersionScheme));
        Assert.Equal(["pkg:npm/left-pad-ng"], proposal?.NewProduct?.Packages);
    }

    [Fact]
    public void A_name_alone_that_no_entry_has_is_left_to_the_model()
    {
        Assert.Null(RuleMapping.Map(Statement("Acme", "Widget Server"), Catalog, Keys));
    }

    [Fact]
    public void A_new_entry_never_takes_a_name_or_key_an_entry_has()
    {
        // «PostgreSQL» under another vendor with an unknown CPE pair: the name is taken, so the rule makes nothing.
        Assert.Null(RuleMapping.Map(Statement("Some Fork Ltd", "PostgreSQL", cpe: ["somefork:pg"]), Catalog, Keys));
    }

    [Theory]
    [InlineData("Widget 2.4.1", "Widget")]
    [InlineData("Widget v3", "Widget")]
    [InlineData("Widget version 5", "Widget")]
    [InlineData("Acme Widget", "Widget")]
    public void Variants_drop_versions_and_the_vendors_name(string product, string variant) =>
        Assert.Contains(variant, RuleMapping.Variants("Acme", product));

    private static UnknownProductStatement Statement(string? vendor, string product, string[]? cpe = null, string? purl = null) =>
        new(ProductKey.Create(vendor, product).Value, vendor, product, cpe ?? [], purl, 3, false);
}
