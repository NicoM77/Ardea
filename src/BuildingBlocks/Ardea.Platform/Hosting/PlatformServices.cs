using Ardea.Platform.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ardea.Platform.Hosting;

public static class PlatformServices
{
    public static IServiceCollection AddArdeaPlatform(this IServiceCollection services, IConfiguration configuration, string role)
    {
        var database = DatabaseOptions.FromConfiguration(configuration);
        services.AddSingleton(database);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(database.AppConnectionString($"ardea-{role}")).Build());
        return services;
    }
}
