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
}

/// <summary>Creates the context for a schema; used by modules to implement <see cref="IModuleDatabase"/>.</summary>
public sealed class ModuleDatabase<TContext>(string schema, Func<DbContextOptions<TContext>, TContext> factory, IReadOnlyCollection<string>? appendOnlyTables = null)
    : IModuleDatabase
    where TContext : DbContext
{
    public string Schema => schema;

    public IReadOnlyCollection<string> AppendOnlyTables => appendOnlyTables ?? [];

    public DbContext CreateContext(DbConnection connection) =>
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
