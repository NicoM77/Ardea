using Microsoft.Extensions.Configuration;

namespace Ardea.Platform.Hosting;

public sealed record WorkerIdentity(string Id)
{
    public static WorkerIdentity FromConfiguration(IConfiguration configuration) =>
        new(configuration["WORKER_ID"] is { Length: > 0 } id ? id : Environment.MachineName);
}
