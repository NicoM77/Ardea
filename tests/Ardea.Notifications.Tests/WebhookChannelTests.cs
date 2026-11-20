using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ardea.Platform.Http;
using Ardea.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace Ardea.Notifications.Tests;

/// <summary>
/// The webhook channel (master plan 8.5, 8.8, 9.5; task 5.6): every message is signed with the shared secret,
/// targets in the private network need the instance administrator's release, loopback and metadata addresses
/// never work, and an answer of 4xx ends the retries while 5xx does not.
/// </summary>
public sealed class WebhookChannelTests(PostgresServer server)
{
    [Fact]
    public async Task A_webhook_target_in_the_private_network_is_refused_without_release()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);

        foreach (var url in new[] { "http://10.11.12.13/hook", "http://192.168.1.20:8080/hook", "http://127.0.0.1:9000/hook", "http://169.254.169.254/latest/meta-data", "http://[::1]/hook" })
        {
            var refused = await s.Owner.PostAsync($"{s.Path}/endpoints", new { name = "Internal", url });
            Assert.True(refused.StatusCode == HttpStatusCode.BadRequest, $"{url}: {(int)refused.StatusCode}");
        }

        // The instance administrator releases one internal host; loopback and metadata addresses stay closed.
        var release = await s.Owner.SendAsync(HttpMethod.Patch, "/api/v1/instance/settings", new { webhookInternalHosts = "10.11.12.13" });
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await s.Owner.PostAsync($"{s.Path}/endpoints", new { name = "Internal", url = "http://10.11.12.13/hook" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync($"{s.Path}/endpoints", new { name = "Local", url = "http://127.0.0.1:9000/hook" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync($"{s.Path}/endpoints", new { name = "Other", url = "http://10.11.12.14/hook" })).StatusCode);
    }

    [Fact]
    public async Task A_receiver_verifies_every_webhook_with_the_shared_secret()
    {
        var address = PrivateAddress();
        await using var receiver = await Receiver.StartAsync(address);
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        Assert.Equal(HttpStatusCode.OK, (await s.Owner.SendAsync(HttpMethod.Patch, "/api/v1/instance/settings", new { webhookInternalHosts = address.ToString() })).StatusCode);
        var created = await NotifyTestHost.JsonAsync(
            await s.Owner.PostAsync($"{s.Path}/endpoints", new { name = "SIEM", url = $"http://{address}:{receiver.Port}/hooks/T0001/s3cr3t-in-the-path" }), HttpStatusCode.Created);
        var secret = created.GetProperty("secret").GetString()!;
        var endpoint = created.GetProperty("id").GetGuid();
        Assert.StartsWith("whsec_", secret, StringComparison.Ordinal);
        Assert.Equal(address.ToString(), created.GetProperty("host").GetString());
        // The secret is shown once; the list never carries it, and the database holds it encrypted.
        Assert.Equal(JsonValueKind.Null, (await NotifyTestHost.JsonAsync(await s.Owner.GetAsync($"{s.Path}/endpoints")))[0].GetProperty("secret").ValueKind);
        Assert.DoesNotContain(secret, await host.ScalarAsync<string>("SELECT secret_enc FROM notify.endpoint"), StringComparison.Ordinal);

        var test = await NotifyTestHost.JsonAsync(await s.Owner.PostAsync($"{s.Path}/endpoints/{endpoint}/test"));
        Assert.True(test.GetProperty("ok").GetBoolean(), test.ToString());

        await s.RuleAsync("Machines", 2, ["action.raised", "action.level_raised"], Scenario.Target("webhook", kind: "endpoint", reference: endpoint.ToString()));
        var action = await s.ActionAsync(2, level: 5, exploitation: "active");
        await s.EventAsync("action.raised", action);
        await s.EvaluateAsync();
        await s.DeliverAsync();

        Assert.Equal(2, receiver.Requests.Count);
        foreach (var request in receiver.Requests)
        {
            Assert.True(Verify(request, secret), "The signature does not match the body.");
            Assert.False(Verify(request with { Body = request.Body.Replace("Acme", "Evil", StringComparison.Ordinal) }, secret));
        }

        var payload = JsonDocument.Parse(receiver.Requests.Last().Body).RootElement;
        Assert.Equal("ardea.notification.v1", payload.GetProperty("schema").GetString());
        var item = payload.GetProperty("events")[0];
        Assert.Equal("action.raised", item.GetProperty("event").GetString());
        Assert.Equal(5, item.GetProperty("action").GetProperty("level").GetInt32());
        Assert.Equal("https://ardea.test/o/acme/reminders/2", item.GetProperty("url").GetString());
        Assert.Equal("sent", await host.ScalarAsync<string>("SELECT state FROM notify.delivery WHERE channel = 'webhook'"));
        Assert.Equal("200", await host.ScalarAsync<string>("SELECT last_status FROM notify.endpoint"));

        // Chat services carry the webhook's secret in its path: no log line names the address.
        Assert.DoesNotContain(host.Logs.Messages, m => m.Contains("s3cr3t", StringComparison.Ordinal));

        // 503 is worth another attempt; 410 ends the retries.
        receiver.Answers.Enqueue(503);
        receiver.Answers.Enqueue(410);
        var second = await s.ActionAsync(3, level: 4);
        await s.EventAsync("action.raised", second);
        await s.EvaluateAsync();
        host.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.Single(await host.RunJobsAsync());
        Assert.Equal("failed", await host.ScalarAsync<string>("SELECT state FROM notify.delivery WHERE channel = 'webhook' ORDER BY created_at DESC LIMIT 1"));
        host.Time.Advance(TimeSpan.FromSeconds(30));
        Assert.Empty(await host.RunJobsAsync());
        Assert.Equal("dead|The endpoint answered 410.", await host.ScalarAsync<string>("SELECT state || '|' || last_error FROM notify.delivery WHERE channel = 'webhook' ORDER BY created_at DESC LIMIT 1"));
    }

    /// <summary>What a receiver does: HMAC-SHA256 over «{t}.{body}» with the shared secret, compared in constant time.</summary>
    private static bool Verify(Received request, string secret)
    {
        var parts = request.Signature.Split(',').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{parts["t"]}.{request.Body}"));
        return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(parts["v1"]));
    }

    /// <summary>An address of this machine in a private range: the test listener is reached like an internal target.</summary>
    private static IPAddress PrivateAddress() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && OutboundAddresses.IsInternal(a) && !OutboundAddresses.IsForbidden(a))
        ?? throw new InvalidOperationException("This machine has no private IPv4 address for the webhook receiver.");

    private sealed record Received(string Signature, string Body);

    /// <summary>A local HTTP listener that records what it receives and answers with the queued status codes, else 200.</summary>
    private sealed class Receiver : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private Receiver(WebApplication app, int port)
        {
            _app = app;
            Port = port;
        }

        public int Port { get; }

        public ConcurrentQueue<Received> Requests { get; } = new();

        public ConcurrentQueue<int> Answers { get; } = new();

        public static async Task<Receiver> StartAsync(IPAddress address)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(k => k.Listen(address, 0, o => o.Protocols = HttpProtocols.Http1));
            var app = builder.Build();
            Receiver? self = null;
            app.MapPost("/{**path}", async (HttpRequest request) =>
            {
                using var reader = new StreamReader(request.Body);
                self!.Requests.Enqueue(new Received(request.Headers["Ardea-Signature"].ToString(), await reader.ReadToEndAsync()));
                return Results.StatusCode(self.Answers.TryDequeue(out var status) ? status : 200);
            });
            await app.StartAsync();
            var port = new Uri(app.Urls.First()).Port;
            self = new Receiver(app, port);
            return self;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
