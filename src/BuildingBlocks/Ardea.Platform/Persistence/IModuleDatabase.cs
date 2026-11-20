using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Platform.Persistence;

public interface IModuleDatabase
{
    string Schema { get; }

    IReadOnlyCollection<string> AppendOnlyTables => [];

    DbContext CreateContext(DbConnection connection);

    Task<int> SeedAsync(DbConnection connection, MasterDataOptions masterData, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}

public sealed class ModuleDatabase<TContext>(
    string schema,
    Func<DbContextOptions<TContext>, TContext> factory,
    IReadOnlyCollection<string>? appendOnlyTables = null,
    Func<TContext, MasterDataOptions, CancellationToken, Task<int>>? seed = null)
    : IModuleDatabase
    where TContext : DbContext
{
    public string Schema => schema;

    public IReadOnlyCollection<string> AppendOnlyTables => appendOnlyTables ?? [];

    public DbContext CreateContext(DbConnection connection) => Create(connection);

    public async Task<int> SeedAsync(DbConnection connection, MasterDataOptions masterData, CancellationToken cancellationToken)
    {
        if (seed is null)
        {
            return 0;
        }

        await using var context = Create(connection);
        return await seed(context, masterData, cancellationToken);
    }

    private TContext Create(DbConnection connection) =>
        factory(new DbContextOptionsBuilder<TContext>().UseArdeaNpgsql(connection, schema).Options);
}

public static class PlatformDatabase
{
    public static IModuleDatabase Instance { get; } = new ModuleDatabase<PlatformDbContext>(
        PlatformDbContext.Schema,
        options => new PlatformDbContext(options),
        ["audit_event"]);
}
