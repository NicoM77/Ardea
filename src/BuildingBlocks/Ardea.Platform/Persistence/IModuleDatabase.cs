using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Platform.Persistence;

/// <summary>
/// The database of one module: its schema and how to create its <see cref="DbContext"/> for
/// migrations and schema checks. Every module owns exactly one schema (master plan 3.2, 4.11).
/// </summary>
public interface IModuleDatabase
{
    string Schema { get; }

    /// <summary>Tables in this schema the application role may only read and insert into.</summary>
    IReadOnlyCollection<string> AppendOnlyTables => [];

    DbContext CreateContext(DbConnection connection);

    /// <summary>
    /// Loads this schema's master data after every schema is migrated (master plan 4.11). Runs on
    /// every <c>migrate</c>, so it must be idempotent. Returns the number of rows it changed.
    /// </summary>
    Task<int> SeedAsync(DbConnection connection, MasterDataOptions masterData, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}

/// <summary>
/// Creates the context for a schema; used by modules to implement <see cref="IModuleDatabase"/>. The
/// optional <c>seed</c> loads master data through a context on the owner connection (see
/// <see cref="IModuleDatabase.SeedAsync"/>).
/// </summary>
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

/// <summary>The platform's own schema.</summary>
public static class PlatformDatabase
{
    public static IModuleDatabase Instance { get; } = new ModuleDatabase<PlatformDbContext>(
        PlatformDbContext.Schema,
        options => new PlatformDbContext(options),
        ["audit_event"]);
}
