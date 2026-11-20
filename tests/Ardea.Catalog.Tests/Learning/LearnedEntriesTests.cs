using Ardea.Catalog.Contracts;
using Ardea.Catalog.Products;
using Ardea.Catalog.Tests.Products;

namespace Ardea.Catalog.Tests.Learning;

public sealed class LearnedEntriesTests
{
    private static readonly IReadOnlyList<CatalogProduct> Curated = CatalogFiles.Load(CatalogFilesTests.RepositoryCatalog).Products;

    private static readonly IReadOnlyList<CatalogProduct> Twins =
    [
        .. Curated.Select(p => p with
        {
            Key = "learned-" + p.Key.Replace('/', '-'),
            Origin = ProductOrigin.Learned,
            Kind = ProductKind.Other,
            VersionScheme = VersionSchemeNames.Numeric,
            RelatedKeys = [],
            PlatformKey = null,
        }),
    ];

    private static readonly CatalogLayers CuratedOnly = new(Curated);

    private static readonly CatalogLayers WithLearned = new(
        Curated.Concat(Twins),
        Curated.Take(40).Select((p, i) => (Raw: RawKeyOf(p), Target: Curated[(i + 7) % Curated.Count].Key))
            .Where(x => x.Raw is not null).DistinctBy(x => x.Raw).ToDictionary(x => x.Raw!, x => x.Target));

    [Fact]
    public void A_learned_entry_changes_no_curated_entry()
    {
        foreach (var product in Curated)
        {
            Assert.Equal(product, WithLearned.FindByKey(product.Key));
            Assert.Equal(product, WithLearned.Curated.FindByKey(product.Key));
        }

        Assert.Equal(Curated.Count, WithLearned.Curated.Products.Count);
    }

    [Fact]
    public void Every_spelling_cpe_pair_package_and_image_of_the_curated_catalogue_finds_what_it_found_before()
    {
        var checks = 0;
        foreach (var product in Curated)
        {
            foreach (var (vendor, name) in Spellings(product))
            {
                Assert.Equal(CuratedOnly.FindByName(vendor, name)?.Product.Key, WithLearned.FindByName(vendor, name)?.Product.Key);
                Assert.Equal(CuratedOnly.Resolve(vendor, name, null, null), WithLearned.Resolve(vendor, name, null, null));
                checks++;
            }

            foreach (var pair in product.CpePairs)
            {
                Assert.Equal(CuratedOnly.FindByCpe(pair)?.Key, WithLearned.FindByCpe(pair)?.Key);
                Assert.Equal(CuratedOnly.Resolve(pair.Vendor, pair.Product, pair, null), WithLearned.Resolve(pair.Vendor, pair.Product, pair, null));
                checks++;
            }

            foreach (var package in product.Packages)
            {
                Assert.Equal(CuratedOnly.FindByPackage(package.Type, package.Name)?.Key, WithLearned.FindByPackage(package.Type, package.Name)?.Key);
                Assert.Equal(CuratedOnly.Resolve(null, package.Name, null, package), WithLearned.Resolve(null, package.Name, null, package));
                checks++;
            }

            foreach (var image in product.ImageNames)
            {
                Assert.Equal(CuratedOnly.FindByImage(image)?.Key, WithLearned.FindByImage(image)?.Key);
                checks++;
            }
        }

        Assert.True(checks > 1000, $"Only {checks} lookups were checked.");
    }

    [Fact]
    public void A_raw_key_keeps_finding_what_it_found_and_a_learned_one_says_so()
    {
        Assert.Equal(new ResolvedProduct(ProductKey.Create("Acme", "Widget"), null), CuratedOnly.Resolve("Acme", "Widget", null, null));

        var learned = new CatalogLayers(
            Curated.Append(new CatalogProduct
            {
                Key = "acme-widget",
                Name = "Acme Widget",
                Vendor = "Acme",
                Kind = ProductKind.Other,
                VersionScheme = VersionSchemeNames.Numeric,
                CpePairs = [new CpePair("acme", "widget")],
                Aliases = [new ProductAlias("Acme", ["Widget"])],
                Origin = ProductOrigin.Learned,
            }),
            new Dictionary<string, string> { ["acme/gizmo"] = "postgresql" });

        Assert.Equal(new ResolvedProduct(Key("acme-widget"), "acme/widget"), learned.Resolve("Acme", "Widget", null, null));
        Assert.Equal(new ResolvedProduct(Key("acme-widget"), "acme/widgetserver"), learned.Resolve("Acme", "Widget Server", new CpePair("acme", "widget"), null));
        Assert.Equal(new ResolvedProduct(Key("postgresql"), "acme/gizmo"), learned.Resolve("Acme", "Gizmo", null, null));
        Assert.True(learned.FindByName("Acme", "Gizmo")!.Learned);
        Assert.False(learned.FindByName("n/a", "PostgreSQL")!.Learned);
    }

    private static IEnumerable<(string? Vendor, string Name)> Spellings(CatalogProduct product)
    {
        yield return (product.Vendor, product.Name);
        yield return (null, product.Name);
        foreach (var alias in product.Aliases)
        {
            foreach (var name in alias.Names.Where(n => !CatalogIndex.IsPattern(n)))
            {
                yield return (alias.Vendor, name);
                yield return (alias.Vendor ?? product.Vendor, name);
            }
        }
    }

    private static ProductKey Key(string value) => ProductKey.TryParse(value, out var key) ? key : throw new ArgumentException(value);

    private static string? RawKeyOf(CatalogProduct product) => ProductKey.TryCreate("Learned Vendor", product.Name, out var key) ? key.Value : null;
}
