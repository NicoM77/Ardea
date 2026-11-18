using Microsoft.Extensions.Configuration;

namespace Ardea.Platform.Hosting;

/// <summary>
/// Identifies a worker process for leases and its heartbeat. In a container the host name is unique
/// and also known to the health check process; <c>ARDEA_WORKER_ID</c> overrides it.
/// </summary>
public sealed record WorkerIdentity(string Id)
{
    public static WorkerIdentity FromConfiguration(IConfiguration configuration) =>
        new(configuration["WORKER_ID"] is { Length: > 0 } id ? id : Environment.MachineName);
}
