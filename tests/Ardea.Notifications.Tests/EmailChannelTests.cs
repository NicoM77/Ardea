using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Ardea.TestSupport;
using DotNet.Testcontainers.Builders;

namespace Ardea.Notifications.Tests;

/// <summary>
/// The e-mail channel (master plan 8.5, 8.6, 8.8; task 5.4) against a real mail server: an unreachable server
/// delays the message, the job system retries it, and it arrives with level and number in the subject and
/// the unsubscribe header.
/// </summary>
public sealed class EmailChannelTests(PostgresServer server)
{
    [Fact]
    public async Task An_unreachable_smtp_server_delays_the_delivery_but_does_not_lose_it()
    {
        await using var mailpit = new ContainerBuilder("axllent/mailpit:v1.27")
            .WithPortBinding(1025, true)
            .WithPortBinding(8025, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPath("/readyz").ForPort(8025)))
            .Build();
        await mailpit.StartAsync(TestContext.Current.CancellationToken);
        await using var host = await NotifyTestHost.StartAsync(server, captureMail: false);
        var s = await Scenario.StartAsync(host);
        var closed = ClosedPort();
        var smtp = await s.Owner.SendAsync(HttpMethod.Patch, "/api/v1/instance/settings", new
        {
            smtpHost = mailpit.Hostname, smtpPort = closed.ToString(System.Globalization.CultureInfo.InvariantCulture), smtpFrom = "ardea@example.org", smtpSecurity = "none",
        });
        Assert.Equal(HttpStatusCode.OK, smtp.StatusCode);
        await s.RuleAsync("High", 4, ["action.raised"], Scenario.Target("email"));
        var action = await s.ActionAsync(2, level: 4);
        await s.EventAsync("action.raised", action);
        await s.EvaluateAsync();

        // The server is down: the attempt fails, the message waits for the next one.
        host.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.Single(await host.RunJobsAsync());
        Assert.Equal("failed|1", await host.ScalarAsync<string>("SELECT state || '|' || attempts FROM notify.delivery"));
        Assert.Equal("pending", await host.ScalarAsync<string>("SELECT state FROM platform.job WHERE kind = 'notify.deliver'"));

        // The server is back: the retry after the back-off delivers it.
        var back = await s.Owner.SendAsync(HttpMethod.Patch, "/api/v1/instance/settings", new { smtpPort = mailpit.GetMappedPublicPort(1025).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        host.Time.Advance(TimeSpan.FromSeconds(30));
        Assert.Empty(await host.RunJobsAsync());
        Assert.Equal("sent|2", await host.ScalarAsync<string>("SELECT state || '|' || attempts FROM notify.delivery"));

        using var http = new HttpClient { BaseAddress = new Uri($"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(8025)}") };
        var message = (await http.GetFromJsonAsync<JsonElement>("/api/v1/messages", TestContext.Current.CancellationToken)).GetProperty("messages")[0];
        Assert.Equal("[4 High] #2 PostgreSQL in Platform Ops", message.GetProperty("Subject").GetString());
        var id = message.GetProperty("ID").GetString();
        var full = await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{id}", TestContext.Current.CancellationToken);
        var text = full.GetProperty("Text").GetString()!;
        Assert.Contains("What happened", text, StringComparison.Ordinal);
        Assert.Contains("Update to 16.4.", text, StringComparison.Ordinal);
        Assert.Contains("https://ardea.test/o/acme/actions/2", text, StringComparison.Ordinal);
        var headers = await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{id}/headers", TestContext.Current.CancellationToken);
        Assert.Equal("<https://ardea.test/o/acme/settings/notifications>", headers.GetProperty("List-Unsubscribe")[0].GetString());
    }

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
