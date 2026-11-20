using Ardea.Platform.Hosting;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Secrets;

namespace Ardea.Host.Commands;

internal static class CommandServices
{
    public static ServiceProvider Build(IConfiguration configuration, string role, LogLevel minimumLevel = LogLevel.Information)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            HostConfiguration.AddJsonLogging(logging);
            logging.SetMinimumLevel(minimumLevel);
        });
        services.AddSingleton(configuration);
        services.AddArdeaPlatform(configuration, role);
        services.AddUnitOfWork();
        services.AddJobQueue();
        services.AddSourceAdapters();
        services.AddArdeaSecrets(MasterKey.FromBase64(configuration[HostConfiguration.MasterKeyKey]!));
        foreach (var module in Modules.All)
        {
            module.AddServices(services, configuration);
        }

        return services.BuildServiceProvider(validateScopes: true);
    }
}
