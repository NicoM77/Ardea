using Ardea.Platform.Hosting;
using Ardea.Platform.Persistence;
using Npgsql;

namespace Ardea.Host.Commands;

internal static class HealthcheckCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var target = args.Length > 0 ? args[0] : "api";
        var configuration = HostConfiguration.Load();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            return target switch
            {
                "api" => await CheckApiAsync(configuration, cancellation.Token),
                "worker" => await CheckWorkerAsync(configuration, cancellation.Token),
                _ => HelpCommand.Run($"healthcheck {target}"),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or NpgsqlException or OperationCanceledException or TimeoutException)
        {
            await Console.Error.WriteLineAsync($"unhealthy: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> CheckApiAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var port = int.TryParse(configuration["HTTP_PORT"], out var p) ? p : 8080;
        using var http = new HttpClient();
        using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}/health/ready"), cancellationToken);
        return response.IsSuccessStatusCode ? 0 : 1;
    }

    private static async Task<int> CheckWorkerAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var options = DatabaseOptions.FromConfiguration(configuration);
        await using var dataSource = NpgsqlDataSource.Create(options.AppConnectionString("ardea-healthcheck"));
        var worker = WorkerIdentity.FromConfiguration(configuration);
        return await WorkerHeartbeat.IsAliveAsync(dataSource, worker.Id, TimeProvider.System, cancellationToken) ? 0 : 1;
    }
}
