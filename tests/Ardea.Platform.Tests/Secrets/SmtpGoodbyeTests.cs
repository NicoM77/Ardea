using System.Net;
using System.Net.Sockets;
using System.Text;
using Ardea.Platform.Email;
using Ardea.Platform.Secrets;
using Ardea.Platform.Tests.Jobs;
using Ardea.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Tests.Secrets;

public sealed class SmtpGoodbyeTests(PostgresServer server)
{
    [Fact(Timeout = JobTestHost.TestTimeout)]
    public async Task A_server_that_drops_the_connection_after_accepting_the_message_counts_as_sent()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accepted = new TaskCompletionSource();
        var serving = ServeAsync(listener, accepted, cancellation);
        var environment = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SMTP:HOST"] = "127.0.0.1",
            ["SMTP:PORT"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SMTP:FROM"] = "ardea@example.org",
            ["SMTP:SECURITY"] = "none",
        }).Build();
        await using var host = await JobTestHost.CreateAsync(server, s => s.AddLogging().AddArdeaSecrets(new MasterKey(new byte[32])).AddSingleton<IConfiguration>(environment));
        await using var scope = host.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(
            new EmailMessage("person@example.org", "Subject", "Text", "<p>Html</p>"), cancellation);

        Assert.True(accepted.Task.IsCompleted);
        await serving;
    }

    private static async Task ServeAsync(TcpListener listener, TaskCompletionSource accepted, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        async Task Reply(string line) => await stream.WriteAsync(Encoding.ASCII.GetBytes(line + "\r\n"), cancellationToken);

        await Reply("220 fake ESMTP");
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var command = line.Split(' ')[0].ToUpperInvariant();
            switch (command)
            {
                case "EHLO" or "HELO":
                    await Reply("250 fake");
                    break;
                case "DATA":
                    await Reply("354 go ahead");
                    while (await reader.ReadLineAsync(cancellationToken) is { } data && data != ".")
                    {
                    }

                    await Reply("250 queued");
                    accepted.SetResult();
                    break;
                case "QUIT":
                    client.Client.Close();
                    return;
                default:
                    await Reply("250 ok");
                    break;
            }
        }
    }
}
