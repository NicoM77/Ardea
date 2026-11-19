using System.Globalization;
using System.Net.Sockets;
using Ardea.Platform.Ai;

namespace Ardea.Host.Commands;

internal enum DoctorLevel
{
    Ok,
    Warn,
    Fail,
}

internal sealed record DoctorResult(string Check, DoctorLevel Level, string Detail);

/// <summary>
/// The reachability checks of <c>ardea doctor</c> (master plan 12.8): the SMTP server and the language model
/// providers named in the environment, and the hosts of every source adapter. A wrong provider setting fails;
/// a service that cannot be reached is a warning, because it may be down only for now. Only a connection is
/// opened: nothing is sent, and no secret is read or shown. Settings made in the administration instead of the
/// environment are not seen here.
/// </summary>
internal static class DoctorServiceChecks
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    /// <param name="configuration">The environment's settings.</param>
    /// <param name="probe">Opens a connection to a host and port; <c>null</c> when it worked, otherwise why not.</param>
    /// <param name="sourceHosts">The hosts each source adapter may reach, by source key.</param>
    /// <param name="cancellationToken">Cancels the checks.</param>
    public static async Task<IReadOnlyList<DoctorResult>> RunAsync(
        IConfiguration configuration, Func<string, int, CancellationToken, Task<string?>> probe,
        IReadOnlyDictionary<string, IReadOnlyList<string>> sourceHosts, CancellationToken cancellationToken)
    {
        var results = new List<DoctorResult>();

        if (configuration["SMTP:HOST"] is { Length: > 0 } smtpHost)
        {
            var port = int.TryParse(configuration["SMTP:PORT"], NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : 587;
            results.Add(await ReachAsync("smtp", $"{smtpHost}:{port}", smtpHost, port, probe, cancellationToken));
        }
        else
        {
            results.Add(new DoctorResult("smtp", DoctorLevel.Ok, "not set in the environment (the administration may set it; without it, e-mail is off)"));
        }

        var providers = AiProviderOptions.FromConfiguration(configuration, out var errors);
        if (errors.Count > 0)
        {
            results.Add(new DoctorResult("ai", DoctorLevel.Fail, string.Join(" ", errors)));
        }
        else if (providers.Count == 0)
        {
            results.Add(new DoctorResult("ai", DoctorLevel.Ok, "no provider set: alerts are assessed by rules and templates"));
        }

        foreach (var provider in errors.Count == 0 ? providers : [])
        {
            var endpoint = provider.Endpoint ?? new Uri("https://api.anthropic.com");
            results.Add(await ReachAsync($"ai {provider.Name}", $"{provider.Kind} {endpoint.Host}:{endpoint.Port}", endpoint.Host, endpoint.Port, probe, cancellationToken));
        }

        foreach (var (key, hosts) in sourceHosts)
        {
            var failed = new List<string>();
            foreach (var host in hosts)
            {
                if (await probe(host, 443, cancellationToken) is { } why)
                {
                    failed.Add($"{host} ({why})");
                }
            }

            results.Add(failed.Count == 0
                ? new DoctorResult($"source {key}", DoctorLevel.Ok, $"{hosts.Count} host(s) reachable")
                : new DoctorResult($"source {key}", DoctorLevel.Warn, "not reachable: " + string.Join(", ", failed)));
        }

        return results;
    }

    /// <summary>A plain TCP connection with a time limit.</summary>
    public static async Task<string?> TryConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "timed out";
        }
        catch (SocketException ex)
        {
            return ex.SocketErrorCode.ToString();
        }
    }

    private static async Task<DoctorResult> ReachAsync(
        string check, string what, string host, int port, Func<string, int, CancellationToken, Task<string?>> probe, CancellationToken cancellationToken) =>
        await probe(host, port, cancellationToken) is { } why
            ? new DoctorResult(check, DoctorLevel.Warn, $"{what} not reachable: {why}")
            : new DoctorResult(check, DoctorLevel.Ok, $"{what} reachable");
}
