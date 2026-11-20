using Ardea.Platform.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Modules;

public interface IModule
{
    string Name { get; }

    IModuleDatabase? Database => null;

    void AddServices(IServiceCollection services, IConfiguration configuration);

    void AddApiServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    void MapEndpoints(IEndpointRouteBuilder endpoints);

    void AddWorkerServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    IReadOnlyList<MaintenanceCommand> Commands => [];
}
