using Ardea.Platform.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Modules;

/// <summary>
/// Entry point of a module. The host discovers nothing by reflection: it lists the modules
/// explicitly and calls these members. Everything else in a module assembly stays internal.
/// </summary>
public interface IModule
{
    /// <summary>Name of the module; also the name of its database schema.</summary>
    string Name { get; }

    /// <summary>The module's schema, or <c>null</c> while it has no tables yet.</summary>
    IModuleDatabase? Database => null;

    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Services only the <c>api</c> role needs: authentication, rate limits, hosted helpers of the API.</summary>
    void AddApiServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
