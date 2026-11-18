using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Ardea.Platform.Persistence;

/// <summary>Common EF Core setup for every module: snake_case names, migrations history in the module schema.</summary>
public static class DbContextOptionsExtensions
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static TBuilder UseArdeaNpgsql<TBuilder>(
        this TBuilder builder, string connectionString, string schema, Action<NpgsqlDbContextOptionsBuilder>? npgsql = null)
        where TBuilder : DbContextOptionsBuilder
    {
        builder.UseNpgsql(connectionString, o => Configure(o, schema, npgsql)).UseSnakeCaseNamingConvention();
        return builder;
    }

    public static TBuilder UseArdeaNpgsql<TBuilder>(
        this TBuilder builder, DbConnection connection, string schema, Action<NpgsqlDbContextOptionsBuilder>? npgsql = null)
        where TBuilder : DbContextOptionsBuilder
    {
        builder.UseNpgsql(connection, o => Configure(o, schema, npgsql)).UseSnakeCaseNamingConvention();
        return builder;
    }

    private static void Configure(NpgsqlDbContextOptionsBuilder options, string schema, Action<NpgsqlDbContextOptionsBuilder>? npgsql)
    {
        options.MigrationsHistoryTable(MigrationsHistoryTable, schema);
        npgsql?.Invoke(options);
    }
}
