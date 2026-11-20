using Ardea.Catalog.Contracts;
using Ardea.Catalog.Products;

namespace Ardea.Catalog.Tests.Products;

public sealed class CatalogIndexTests
{
    private static readonly CatalogIndex Index = new(CatalogFiles.Load(CatalogFilesTests.RepositoryCatalog).Products);

    [Theory]
    [InlineData("Microsoft Corporation", "ASP.NET Core", "aspnetcore")]
    [InlineData("microsoft", "asp.net_core", "aspnetcore")]
    [InlineData("n/a", "PostgreSQL", "postgresql")]
    [InlineData(null, "postgres", "postgresql")]
    [InlineData("PostgreSQL Global Development Group", "PostgreSQL", "postgresql")]
    [InlineData("EnterpriseDB", "PostgreSQL", "postgresql")]
    [InlineData("nginx, Inc.", "NGINX", "nginx")]
    [InlineData("F5 Networks", "NGINX Open Source", "nginx")]
    [InlineData("The Apache Software Foundation", "Apache Log4j2", "log4j-core")]
    [InlineData("Red Hat", "openssl", "rhel/openssl")]
    [InlineData("n/a", "OpenSSL", "openssl")]
    [InlineData("OpenSSL Project", "OpenSSL", "openssl")]
    [InlineData("VMware", "RabbitMQ Server", "rabbitmq")]
    [InlineData("WooCommerce", "WooCommerce", "woocommerce")]
    [InlineData("nodejs", "node", "nodejs")]
    [InlineData("Linux", "Linux", "linux-kernel")]
    [InlineData("Fortinet", "FortiOS", "fortios")]
    [InlineData("Ivanti", "Connect Secure", "ivanti-connect-secure")]
    [InlineData("Pulse Secure", "Pulse Connect Secure", "ivanti-connect-secure")]
    [InlineData("Citrix", "NetScaler ADC", "netscaler-adc")]
    [InlineData("Cisco", "Cisco Adaptive Security Appliance (ASA) Software", "cisco-asa")]
    [InlineData("Team Yoast", "Yoast SEO – Advanced SEO with real-time guidance", "yoast-seo")]
    [InlineData("HashiCorp", "Vault", "vault")]
    [InlineData("n8n-io", "n8n", "n8n")]
    [InlineData("Oracle Corporation", "MySQL Server", "mysql")]
    public void Spellings_of_vendor_and_product_find_the_product(string? vendor, string product, string key) =>
        Assert.Equal(key, Index.FindByName(vendor, product)?.Product.Key);

    [Theory]
    [InlineData("Microsoft", ".NET 10.0 installed on Linux", "dotnet", "10.0", "linux")]
    [InlineData("Microsoft", ".NET 8.0", "dotnet", "8.0", null)]
    [InlineData("Microsoft Corporation", "ASP.NET Core 9.0", "aspnetcore", "9.0", null)]
    [InlineData("Red Hat", "Red Hat Enterprise Linux 9", "rhel", "9", null)]
    [InlineData("Microsoft", "Windows Server 2022 (Server Core installation)", "windows-server", "2022", null)]
    [InlineData("Microsoft", "Windows 11 Version 23H2", "windows", "11", null)]
    [InlineData("Microsoft", "Microsoft SQL Server 2022 (GDR)", "sql-server", "2022", null)]
    [InlineData("Microsoft", "Microsoft Exchange Server 2019 Cumulative Update 14", "exchange-server", "2019", null)]
    public void Name_patterns_give_version_line_and_platform(string vendor, string product, string key, string line, string? platform)
    {
        var match = Index.FindByName(vendor, product);

        Assert.Equal((key, line, platform), (match?.Product.Key, match?.VersionLine, match?.Platform));
    }

    [Theory]
    [InlineData("Acme", "Express")]
    [InlineData("Acme", "ESXi")]
    [InlineData(null, "login.php")]
    [InlineData("Microsoft", "Paint")]
    public void Unknown_or_foreign_spellings_find_nothing(string? vendor, string product) => Assert.Null(Index.FindByName(vendor, product));

    [Fact]
    public void Cpe_pair_finds_the_product()
    {
        Assert.Equal("aspnetcore", Index.FindByCpe(Cpe.Parse("cpe:2.3:a:microsoft:asp.net_core:8.0.0:*:*:*:*:*:*:*").Pair)?.Key);
        Assert.Equal("nodejs", Index.FindByCpe(Cpe.Parse("cpe:/a:nodejs:node.js:20.0.0").Pair)?.Key);
    }

    [Theory]
    [InlineData("npm", "@angular/core", "angular")]
    [InlineData("NuGet", "newtonsoft.json", "newtonsoft-json")]
    [InlineData("Maven", "org.apache.logging.log4j:log4j-core", "log4j-core")]
    [InlineData("PyPI", "Django", "django")]
    [InlineData("Packagist", "laravel/framework", "laravel")]
    [InlineData("Go", "github.com/grafana/grafana", "grafana")]
    [InlineData("Go", "stdlib", "go")]
    [InlineData("NuGet", "Umbraco.Cms", "umbraco")]
    [InlineData("npm", "next", "nextjs")]
    [InlineData("Maven", "org.springframework:spring-webmvc", "spring-framework")]
    [InlineData("PyPI", "Jinja2", "jinja")]
    public void Package_finds_the_product_in_osv_and_purl_spelling(string type, string name, string key) =>
        Assert.Equal(key, Index.FindByPackage(type, name)?.Key);

    [Fact]
    public void Purl_is_read_into_type_name_and_version()
    {
        Assert.True(PackageCoordinate.TryParsePurl("pkg:npm/%40angular/core@18.2.1?foo=bar#src", out var package, out var version));

        Assert.Equal((new PackageCoordinate("npm", "@angular/core"), "18.2.1"), (package, version));
        Assert.Equal("angular", Index.FindByPackage(package.Type, package.Name)?.Key);
    }

    [Theory]
    [InlineData("postgres:18", "postgresql")]
    [InlineData("docker.io/library/postgres:16-alpine", "postgresql")]
    [InlineData("mcr.microsoft.com/dotnet/aspnet:10.0", "aspnetcore")]
    [InlineData("php@sha256:0123456789abcdef", "php")]
    [InlineData("grafana/grafana-oss:11.0.0", "grafana")]
    [InlineData("eclipse-temurin:21-jre", "openjdk")]
    [InlineData("mcr.microsoft.com/mssql/server:2022-latest", "sql-server")]
    [InlineData("quay.io/keycloak/keycloak:26.0", "keycloak")]
    public void Container_image_finds_the_product(string image, string key) => Assert.Equal(key, Index.FindByImage(image)?.Key);

    [Fact]
    public void Search_finds_by_name_alias_and_key_best_first()
    {
        Assert.Equal("postgresql", Index.Search("postgres", 5)[0].Key);
        Assert.Equal("entra-id", Index.Search("azure active", 5).Single().Key);
        Assert.Equal("dotnet", Index.Search(".net", 5)[0].Key);
        Assert.Contains("aspnetcore", Index.Search(".net", 5).Select(p => p.Key));
        Assert.Empty(Index.Search("   ", 5));
        Assert.Empty(Index.Search("postgres", 0));
    }

    [Theory]
    [InlineData("WINDOWS SERVER", "windows-server")]
    [InlineData("Wíndows Server 2022", "windows-server")]
    [InlineData("windows-server", "windows-server")]
    [InlineData("contact form 7", "contact-form-7")]
    [InlineData("mcr.microsoft.com/dotnet/aspnet", "aspnetcore")]
    [InlineData("jackson-databind", "jackson-databind")]
    [InlineData("ivanti connect", "ivanti-connect-secure")]
    [InlineData("sql", "sql-server")]
    public void Search_ignores_case_accents_separators_and_trailing_versions(string text, string key) =>
        Assert.Equal(key, Index.Search(text, 5)[0].Key);

    [Fact]
    public void Search_ranks_exact_spellings_before_prefixes_words_and_typos()
    {
        CatalogProduct[] products =
        [
            new() { Key = "tool", Name = "Tool", Vendor = "Acme", Kind = ProductKind.Library, VersionScheme = "semver" },
            new() { Key = "toolbox", Name = "Toolbox", Vendor = "Acme", Kind = ProductKind.Library, VersionScheme = "semver" },
            new() { Key = "power-tool", Name = "Power Tool", Vendor = "Acme", Kind = ProductKind.Library, VersionScheme = "semver" },
            new() { Key = "gadget", Name = "Gadget", Vendor = "Tool Works", Kind = ProductKind.Library, VersionScheme = "semver" },
            new() { Key = "tolo", Name = "Tolo", Vendor = "Other", Kind = ProductKind.Library, VersionScheme = "semver" },
        ];
        var index = new CatalogIndex(products);

        Assert.Equal(["tool", "power-tool", "toolbox", "gadget", "tolo"], index.Search("tool", 10).Select(p => p.Key));
        Assert.Equal(["tolo", "tool"], index.Search("tolo", 10).Select(p => p.Key));
        Assert.Equal(["toolbox"], index.Search("toolbxo", 10).Select(p => p.Key));
        Assert.Empty(index.Search("xyz", 10));
    }

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("graphana", "grafana", 2)]
    [InlineData("serach", "search", 1)]
    [InlineData("abc", "abc", 0)]
    [InlineData("a", "abcdef", 3)]
    public void Distance_counts_edits_and_swaps_and_stops_past_the_bound(string a, string b, int expected) =>
        Assert.Equal(expected, CatalogSearch.Distance(a, b, 2));

    [Fact]
    public void Learned_entry_adds_spellings_but_never_overrides_a_curated_one()
    {
        CatalogProduct[] products =
        [
            new() { Key = "tool", Name = "Tool", Vendor = "Acme", Kind = ProductKind.Library, VersionScheme = "semver", CpePairs = [new("acme", "tool")] },
            new() { Key = "tool-learned", Name = "Tool", Vendor = "Acme", Kind = ProductKind.Library, VersionScheme = "numeric", Origin = ProductOrigin.Learned, CpePairs = [new("acme", "tool"), new("acme", "tool_pro")] },
            new() { Key = "gadget", Name = "Gadget", Vendor = "Acme", Kind = ProductKind.Other, VersionScheme = "numeric", Origin = ProductOrigin.Learned },
        ];

        var index = new CatalogIndex(products, new Dictionary<string, string> { ["acme/toolkit"] = "tool" });

        Assert.Empty(index.Conflicts);
        Assert.Equal("tool", index.FindByName("Acme", "Tool")?.Product.Key);
        Assert.Equal("tool", index.FindByCpe(new("acme", "tool"))?.Key);
        Assert.Equal("tool-learned", index.FindByCpe(new("acme", "tool_pro"))?.Key);
        Assert.Equal("gadget", index.FindByName("Acme Inc.", "Gadget")?.Product.Key);
        Assert.Equal("tool", index.FindByName("Acme", "Toolkit")?.Product.Key);
    }
}
