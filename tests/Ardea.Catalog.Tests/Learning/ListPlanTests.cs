using Ardea.Catalog.Tests.Products;
using Ardea.Platform.Persistence;
using Ardea.TestSupport;

namespace Ardea.Catalog.Tests.Learning;

/// <summary>
/// The catalogue's lists of unknown products (master plan 6.9, 10.2) are read on a first load with tens of
/// thousands of keys: the list of those still open and the daily choice of what to ask the model read the most
/// frequent first from the index, without sorting every open key. The review list holds only what waits for a
/// person and is small; it still uses its index. Queries are written as EF Core sends them.
/// </summary>
public sealed class ListPlanTests(PostgresServer server)
{
    [Theory]
    // «Open» view and the daily choice: in KEV first, then the most records, then the key.
    [InlineData("SELECT * FROM catalog.unknown_product u WHERE u.outcome = 'open' ORDER BY u.in_kev DESC, u.record_count DESC, u.raw_key LIMIT 100", "ix_unknown_product_outcome_count", true)]
    [InlineData("SELECT u.id FROM catalog.unknown_product u WHERE u.outcome = 'open' AND (u.record_count >= 2 OR u.in_kev) AND (u.last_attempt_at IS NULL OR u.last_attempt_at < now()) ORDER BY u.in_kev DESC, u.record_count DESC, u.raw_key LIMIT 51", "ix_unknown_product_outcome_count", true)]
    // «Review» view: what waits for a person; the link from a hit names one key.
    [InlineData("SELECT * FROM catalog.unknown_product u WHERE u.outcome IN ('pending', 'unclear') ORDER BY u.in_kev DESC, u.record_count DESC, u.raw_key LIMIT 100", "ix_unknown_product_outcome_count", false)]
    [InlineData("SELECT * FROM catalog.unknown_product u WHERE u.raw_key = 'cpe:acme:widget'", "ux_unknown_product_raw_key", true)]
    // The learned entries of the catalogue and the weekly measures.
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
