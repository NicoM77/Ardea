using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Ardea.Platform.Tests.Persistence;
using Ardea.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ardea.Platform.Tests.Tenancy;

/// <summary>
/// Experiment 0.6 (ADR 029): does row-level security work with EF Core, Npgsql, the connection pool
/// and the unit of work's shared transaction? The policies read <c>ardea.org_id</c>, which the unit
/// of work sets transaction-locally. Every query here bypasses the EF tenant filter
/// (<c>IgnoreQueryFilters</c> or raw SQL), so only the database layer separates tenants.
/// </summary>
public sealed class RowLevelSecurityExperimentTests(PostgresServer server)
{
    private static readonly Guid OrgA = Guid.CreateVersion7();
    private static readonly Guid OrgB = Guid.CreateVersion7();

    [Fact]
    public async Task Parallel_units_of_work_on_a_small_pool_only_see_their_own_rows()
    {
        await using var fixture = await RlsFixture.CreateAsync(server, maxPoolSize: 3);
        await fixture.SeedAsync(OrgA, 5);
        await fixture.SeedAsync(OrgB, 7);

        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(async i =>
        {
            var org = i % 2 == 0 ? OrgA : OrgB;
            await using var scope = fixture.Scope(org);
            var context = scope.ServiceProvider.GetRequiredService<ItemContext>();
            var visible = await context.Items.IgnoreQueryFilters().Select(x => x.OrganizationId).Distinct().ToListAsync();
            return (org, visible);
        }));

        Assert.All(results, r => Assert.Equal([r.org], r.visible));
    }

    [Fact]
    public async Task Pooled_connection_carries_no_tenant_into_the_next_unit_of_work()
    {
        await using var fixture = await RlsFixture.CreateAsync(server, maxPoolSize: 1);
        await fixture.SeedAsync(OrgA, 3);

        await using (var first = fixture.Scope(OrgA))
        {
            Assert.Equal(3, await first.ServiceProvider.GetRequiredService<ItemContext>().Items.IgnoreQueryFilters().CountAsync());
            await first.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
        }

        // Same physical connection (pool of one), no organisation: nothing is visible, nothing can be written.
        await using var second = fixture.Scope(organization: null);
        var unitOfWork = second.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var connection = await unitOfWork.GetConnectionAsync(CancellationToken.None);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM rlstest.item", connection);
        Assert.Equal(0L, await count.ExecuteScalarAsync());

        await using var insert = new NpgsqlCommand($"INSERT INTO rlstest.item (id, organization_id, name) VALUES (uuidv7(), '{OrgA}', 'smuggled')", connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal("42501", error.SqlState); // new row violates row-level security policy
    }

    [Fact]
    public async Task Write_into_another_organisation_is_rejected_by_the_policy()
    {
        await using var fixture = await RlsFixture.CreateAsync(server, maxPoolSize: 2);
        await using var scope = fixture.Scope(OrgA);
        var connection = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().GetConnectionAsync(CancellationToken.None);

        await using var insert = new NpgsqlCommand($"INSERT INTO rlstest.item (id, organization_id, name) VALUES (uuidv7(), '{OrgB}', 'foreign')", connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal("42501", error.SqlState);
    }

    [Fact]
    public async Task Two_module_contexts_share_one_transaction_and_its_tenant()
    {
        await using var fixture = await RlsFixture.CreateAsync(server, maxPoolSize: 2);
        await using var scope = fixture.Scope(OrgA);
        var writer = scope.ServiceProvider.GetRequiredService<ItemContext>();
        var reader = scope.ServiceProvider.GetRequiredService<OtherItemContext>();

        writer.Items.Add(new Item { Id = Guid.CreateVersion7(), Name = "written by one module" });
        await writer.SaveChangesAsync();

        // The other context sees the uncommitted row: same connection, same transaction.
        Assert.Equal(1, await reader.Items.IgnoreQueryFilters().CountAsync());
        Assert.Equal(OrgA, (await reader.Items.IgnoreQueryFilters().SingleAsync()).OrganizationId);
    }

    [Fact]
    public async Task Rolled_back_unit_of_work_leaves_nothing_behind()
    {
        await using var fixture = await RlsFixture.CreateAsync(server, maxPoolSize: 1);
        await using (var scope = fixture.Scope(OrgA))
        {
            var context = scope.ServiceProvider.GetRequiredService<ItemContext>();
            context.Items.Add(new Item { Id = Guid.CreateVersion7(), Name = "never committed" });
            await context.SaveChangesAsync();
        }

        await using var check = fixture.Scope(OrgA);
        Assert.Equal(0, await check.ServiceProvider.GetRequiredService<ItemContext>().Items.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Stub_entity_cannot_update_or_delete_another_organisations_row()
    {
        await using var fixture = await RlsFixture.CreateAsync(server, maxPoolSize: 2, rowLevelSecurity: false);
        var foreignId = await fixture.SeedAsync(OrgB, 1);

        // Layer 2 alone (no RLS): the write check and the concurrency token on OrganizationId.
        await using var scope = fixture.Scope(OrgA);
        var context = scope.ServiceProvider.GetRequiredService<ItemContext>();
        context.Remove(new Item { Id = foreignId, OrganizationId = OrgA, Name = "stub" });

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
    }

    private sealed class RlsFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;

        private RlsFixture(ServiceProvider services) => _services = services;

        public static async Task<RlsFixture> CreateAsync(PostgresServer server, int maxPoolSize, bool rowLevelSecurity = true)
        {
            var database = await MigrationDatabase.CreateAsync(server);
            await using (var owner = await database.OpenOwnerAsync())
            {
                await database.Migrator().EnsureAppRoleAsync(owner, CancellationToken.None);
                await using var ddl = new NpgsqlCommand(
                    $"""
                    CREATE SCHEMA rlstest;
                    CREATE TABLE rlstest.item (id uuid PRIMARY KEY, organization_id uuid NOT NULL, name text NOT NULL);
                    {(rowLevelSecurity ? "ALTER TABLE rlstest.item ENABLE ROW LEVEL SECURITY;" : string.Empty)}
                    CREATE POLICY tenant ON rlstest.item
                        USING (organization_id = nullif(current_setting('ardea.org_id', true), '')::uuid)
                        WITH CHECK (organization_id = nullif(current_setting('ardea.org_id', true), '')::uuid);
                    GRANT USAGE ON SCHEMA rlstest TO ardea_app;
                    GRANT SELECT, INSERT, UPDATE, DELETE ON rlstest.item TO ardea_app;
                    """, owner);
                await ddl.ExecuteNonQueryAsync();
            }

            var services = new ServiceCollection();
            services.AddSingleton(_ => new NpgsqlDataSourceBuilder(
                new NpgsqlConnectionStringBuilder(database.Options.AppConnectionString("rls")) { MaxPoolSize = maxPoolSize }.ConnectionString).Build());
            services.AddUnitOfWork();
            services.AddModuleDbContext<ItemContext>("rlstest");
            services.AddModuleDbContext<OtherItemContext>("rlstest");
            return new RlsFixture(services.BuildServiceProvider());
        }

        public AsyncServiceScope Scope(Guid? organization)
        {
            var scope = _services.CreateAsyncScope();
            if (organization is { } org)
            {
                scope.ServiceProvider.GetRequiredService<TenantContext>().SetOrganization(org);
            }

            return scope;
        }

        public async Task<Guid> SeedAsync(Guid organization, int count)
        {
            var last = Guid.Empty;
            await using var scope = Scope(organization);
            var context = scope.ServiceProvider.GetRequiredService<ItemContext>();
            for (var i = 0; i < count; i++)
            {
                last = Guid.CreateVersion7();
                context.Items.Add(new Item { Id = last, Name = $"item {i}" });
            }

            await context.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
            return last;
        }

        public ValueTask DisposeAsync() => _services.DisposeAsync();
    }
}

public sealed class Item : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Name { get; set; }
}

public sealed class ItemContext(DbContextOptions<ItemContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("rlstest");
        modelBuilder.Entity<Item>().ToTable("item");
        ApplyTenantFilters(modelBuilder);
    }
}

public sealed class OtherItemContext(DbContextOptions<OtherItemContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("rlstest");
        modelBuilder.Entity<Item>().ToTable("item");
        ApplyTenantFilters(modelBuilder);
    }
}
