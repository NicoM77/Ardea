using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Tests.Products;

/// <summary>Product keys (master plan 6.5): spellings of one product give one key.</summary>
public sealed class ProductKeyTests
{
    [Theory]
    [InlineData("Microsoft Corporation", "ASP.NET Core", "microsoft/aspnetcore")]
    [InlineData("microsoft", "asp.net_core", "microsoft/aspnetcore")]
    [InlineData("Microsoft", "ASP.NET Core", "microsoft/aspnetcore")]
    [InlineData("The Apache Software Foundation", "Apache HTTP Server", "apache/apachehttpserver")]
    [InlineData("Apache", "Apache HTTP Server", "apache/apachehttpserver")]
    [InlineData("nginx, Inc.", "nginx", "nginx/nginx")]
    [InlineData("F5 Networks", "NGINX", "f5/nginx")]
    [InlineData("Oracle Corporation", "MySQL", "oracle/mysql")]
    [InlineData("The PHP Group", "PHP", "php/php")]
    [InlineData("PostgreSQL Global Development Group", "PostgreSQL", "postgresql/postgresql")]
    [InlineData("Node.js", "Node.js", "nodejs/nodejs")]
    [InlineData("nodejs", "node.js", "nodejs/nodejs")]
    [InlineData("Acme Co., Ltd.", "Acme Router", "acme/acmerouter")]
    [InlineData("Docker Inc.", "Docker Engine", "docker/dockerengine")]
    [InlineData("Société Générale", "Café Manager", "societegenerale/cafemanager")]
    [InlineData("WooCommerce", "WooCommerce Plugin", "woocommerce/woocommerce")]
    [InlineData("Automattic", "WooCommerce – WordPress plugin", "automattic/woocommerce")]
    [InlineData("O'Reilly Media", "Contact Form 7", "oreillymedia/contactform7")]
    public void Spellings_normalise_to_one_key(string vendor, string product, string key) =>
        Assert.Equal(key, ProductKey.Create(vendor, product).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("n/a")]
    [InlineData("N/A")]
    [InlineData("-")]
    [InlineData("unspecified")]
    public void Missing_or_na_vendor_is_ignored(string? vendor)
    {
        // CVE records for PostgreSQL often name vendor «n/a» (master plan 6.5).
        var key = ProductKey.Create(vendor, "PostgreSQL");

        Assert.Equal("postgresql", key.Value);
        Assert.Null(key.Vendor);
    }

    [Fact]
    public void Suffix_removal_never_empties_a_name()
    {
        Assert.Equal("software/foundation", ProductKey.Create("Software", "Foundation").Value);
        Assert.Equal("plugin", ProductKey.Create(null, "Plugin").Value);
    }

    [Fact]
    public void Name_without_letters_or_digits_gives_no_key()
    {
        Assert.False(ProductKey.TryCreate("Acme", "---", out _));
        Assert.Throws<ArgumentException>(() => ProductKey.Create("Acme", "  "));
    }

    [Fact]
    public void Key_exposes_vendor_and_product()
    {
        var key = ProductKey.Create("Microsoft", "ASP.NET Core");

        Assert.Equal(("microsoft", "aspnetcore"), (key.Vendor, key.Product));
    }

    [Theory]
    [InlineData("postgresql", true)]
    [InlineData("microsoft/aspnetcore", true)]
    [InlineData("log4j-core", true)]
    [InlineData("rhel/openssl", true)]
    [InlineData("newtonsoft.json", true)]
    [InlineData("Postgres", false)]
    [InlineData("a//b", false)]
    [InlineData("a/b/c", false)]
    [InlineData("-core", false)]
    [InlineData("core-", false)]
    [InlineData("a--b", false)]
    [InlineData("with space", false)]
    [InlineData("", false)]
    public void Stored_keys_are_checked(string value, bool valid) => Assert.Equal(valid, ProductKey.TryParse(value, out _));
}
