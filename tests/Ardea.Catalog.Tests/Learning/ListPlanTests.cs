using Ardea.Catalog.Tests.Products;
using Ardea.Platform.Persistence;
using Ardea.TestSupport;

namespace Ardea.Catalog.Tests.Learning;

public sealed class ListPlanTests(PostgresServer server)
{
    [Theory]
    [InlineData("SELECT * FROM catalog.unknown_product u WHERE u.outcome = 'open' ORDER BY u.in_kev DESC, u.record_count DESC, u.raw_key LIMIT 100", "ix_unknown_product_outcome_count", true)]
    [InlineData("SELECT u.id FROM catalog.unknown_product u WHERE u.outcome = 'open' AND (u.record_count >= 2 OR u.in_kev) AND (u.last_attempt_at IS NULL OR u.last_attempt_at < now()) ORDER BY u.in_kev DESC, u.record_count DESC, u.raw_key LIMIT 51", "ix_unknown_product_outcome_count", true)]
    [InlineData("SELECT * FROM catalog.unknown_product u WHERE u.outcome IN ('pending', 'unclear') ORDER BY u.in_kev DESC, u.record_count DESC, u.raw_key LIMIT 100", "ix_unknown_product_outcome_count", false)]
    [InlineData("SELECT * FROM catalog.unknown_product u WHERE u.raw_key = 'cpe:acme:widget'", "ux_unknown_product_raw_key", true)]
    [InlineData("SELECT * FROM catalog.product p WHERE p.origin = 'learned' AND p.state = 'active'", "ix_product_origin_state", true)]
    [InlineData("SELECT * FROM catalog.learning_event e WHERE e.kind = 'collected' ORDER BY e.at DESC LIMIT 1", "ix_learning_event_kind_at", true)]
    public async Task Catalogue_lists_use_their_indexes(string sql, string index, bool noSort)
    {
        var db = await CatalogDatabase.CreateAsync(server);
        await using (var owner = await db.OpenOwnerAsync())
        {
            await db.Migrator(new MasterDataOptions(MasterDataOptions.FindDefault())).MigrateAsync(owner, TestContext.Current.CancellationToken);
        }

        var plan = await QueryPlans.ExplainAsync(db.Options.OwnerConnectionString("tests"), sql, TestContext.Current.CancellationToken);

        Assert.True(plan.Any(line => line.Contains(index, StringComparison.Ordinal)), string.Join('\n', plan));
        Assert.True(!noSort || !QueryPlans.SortsEverything(plan), string.Join('\n', plan));
    }
}
