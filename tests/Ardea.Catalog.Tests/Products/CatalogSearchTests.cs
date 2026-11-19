using Ardea.Catalog.Products;
using Ardea.Platform.Persistence;
using Ardea.TestSupport;
using Npgsql;

namespace Ardea.Catalog.Tests.Products;

/// <summary>
/// The acceptance of phase 2: for 50 inputs fixed in advance, spelling variants and typos included,
/// the catalogue search puts the right product first (master plan 14, task 2.4). The search runs
/// through <c>IProductCatalog.SearchAsync</c> over the repository catalogue as <c>migrate</c> loads it.
/// </summary>
public sealed class CatalogSearchTests(PostgresServer server)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly CatalogCache Cache = new(TimeProvider.System);
    private static CatalogDatabase? _database;

    /// <summary>What people type, and the product they mean.</summary>
    public static TheoryData<string, string> Inputs => new()
    {
        // Databases and caches
        { "postgres", "postgresql" },
        { "Postgre SQL", "postgresql" },
        { "postgress", "postgresql" },
        { "ms sql", "sql-server" },
        { "SQL Server 2022", "sql-server" },
        { "maria db", "mariadb" },
        { "mongo", "mongodb" },
        { "elastic serach", "elasticsearch" },
        { "redis", "redis" },

        // Runtimes and operating systems
        { "node", "nodejs" },
        { "NodeJS", "nodejs" },
        { ".net 8", "dotnet" },
        { "java", "openjdk" },
        { "python3", "python" },
        { "php 8.3", "php" },
        { "golang", "go" },
        { "windows server 2019", "windows-server" },
        { "ubuntu 22.04", "ubuntu" },
        { "rhel 9", "rhel" },
        { "alpine", "alpine-linux" },

        // Web servers, containers, messaging, virtualisation
        { "nginix", "nginx" },
        { "apache httpd", "apache-httpd" },
        { "IIS", "iis" },
        { "kubernets", "kubernetes" },
        { "k8s", "kubernetes" },
        { "docker", "docker-engine" },
        { "rabbit mq", "rabbitmq" },
        { "esxi", "esxi" },
        { "vCenter", "vcenter-server" },
        { "proxmox", "proxmox-ve" },
        { "hyperv", "hyper-v" },

        // CMS, shops and frameworks
        { "wordpres", "wordpress" },
        { "typo 3", "typo3" },
        { "magento", "adobe-commerce" },
        { "craft cms", "craft-cms" },
        { "aspnet core", "aspnetcore" },
        { "spring boot", "spring-boot" },
        { "vue js", "vue" },
        { "next.js", "nextjs" },

        // Libraries
        { "log4j2", "log4j-core" },
        { "jackson databind", "jackson-databind" },
        { "newtonsoft", "newtonsoft-json" },

        // Network appliances and tools
        { "fortigate", "fortios" },
        { "pulse secure", "ivanti-connect-secure" },
        { "citrix adc", "netscaler-adc" },
        { "pan os", "pan-os" },
        { "cisco asa", "cisco-asa" },
        { "graphana", "grafana" },
        { "jenkis", "jenkins" },
        { "nextcloud", "nextcloud-server" },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Fifty_distinct_inputs_are_fixed()
    {
        var inputs = Inputs.Select(row => (string)row.Data.Item1).ToList();

        Assert.Equal(50, inputs.Count);
        Assert.Equal(inputs.Count, inputs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public async Task Input_finds_the_product_first(string input, string key)
    {
        await using var app = await OpenSeededAsync();

        var hits = await CatalogDatabase.Lookup(app, Cache).SearchAsync(input, 5, Ct);

        Assert.True(hits.Count > 0 && hits[0].Key == key, $"'{input}' should find '{key}' first, found: {string.Join(", ", hits.Select(h => h.Key))}");
    }

    /// <summary>The application's connection to one database the repository catalogue was migrated into, shared by every case.</summary>
    private async Task<NpgsqlConnection> OpenSeededAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_database is null)
            {
                var database = await CatalogDatabase.CreateAsync(server);
                await using var owner = await database.OpenOwnerAsync();
                await database.Migrator(new MasterDataOptions(MasterDataOptions.FindDefault())).MigrateAsync(owner, Ct);
                _database = database;
            }
        }
        finally
        {
            Gate.Release();
        }

        return await _database.OpenAppAsync();
    }
}
