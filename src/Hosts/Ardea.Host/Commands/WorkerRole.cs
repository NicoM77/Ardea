using Ardea.Platform.Configuration;
using Ardea.Platform.Hosting;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Npgsql;

namespace Ardea.Host.Commands;

/// <summary>The <c>worker</c> role: background jobs (master plan 3.1, 3.4).</summary>
internal static class WorkerRole
{
    public static async Task<int> RunAsync(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        builder.Configuration.AddArdeaEnvironment();
        var check = HostConfiguration.CheckApplication(builder.Configuration);
        if (!check.IsValid)
        {
            return HostConfiguration.Fail(check);
        }

        HostConfiguration.AddJsonLogging(builder.Logging);
        builder.Services.AddArdeaPlatform(builder.Configuration, "worker");
        builder.Services.AddSingleton(WorkerIdentity.FromConfiguration(builder.Configuration));
        builder.Services.AddHostedService(sp => ActivatorUtilities.CreateInstance<WorkerHeartbeat>(sp, AppVersion.Current));
        builder.Services.AddUnitOfWork();
        builder.Services.AddJobWorker();
        foreach (var module in Modules.All)
        {
            module.AddServices(builder.Services, builder.Configuration);
        }

        using var host = builder.Build();
        var behind = await SchemaGate.FindBehindAsync(
            host.Services.GetRequiredService<NpgsqlDataSource>(), host.Services.GetRequiredService<DatabaseOptions>(), CancellationToken.None);
        if (behind.Count > 0)
        {
            await Console.Error.WriteLineAsync($"The database schema is older than this version ({string.Join(", ", behind)}). Run `ardea migrate` first.");
            return 75;
        }

        await host.RunAsync();
        return 0;
    }
}
