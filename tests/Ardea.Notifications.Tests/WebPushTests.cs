using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ardea.Notifications.Delivering;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Ardea.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ardea.Notifications.Tests;

public sealed class WebPushTests(PostgresServer server)
{
    [Fact]
    public async Task The_push_service_receives_an_encrypted_message_signed_with_the_instance_key_and_a_gone_subscription_disappears()
    {
        await using var service = await FakePushService.StartAsync();
        await using var host = await NotifyTestHost.StartAsync(server, services: s => s.AddHttpClient(WebPushChannel.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }));
        var s = await Scenario.StartAsync(host);
        var device = Device.Create();
        var endpoint = $"http://127.0.0.1:{service.Port}/push/device-1";
        await device.InsertAsync(host, await s.OwnerIdAsync(), endpoint);
        var publicKey = (await NotifyTestHost.JsonAsync(await s.Owner.GetAsync("/api/v1/push/public-key"))).GetProperty("publicKey").GetString()!;

        await s.RuleAsync("Phones", 2, ["action.raised"], Scenario.Target("push"));
        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 5, exploitation: "active"), """{"level":5}""");
        await s.EvaluateAsync();
        await s.DeliverAsync();

        var request = Assert.Single(service.Requests);
        Assert.Equal("/push/device-1", request.Path);
        Assert.Equal("aes128gcm", request.Headers["Content-Encoding"]);
        Assert.Equal("86400", request.Headers["TTL"]);
        Assert.Equal("high", request.Headers["Urgency"]);

        var authorization = request.Headers["Authorization"];
        Assert.StartsWith("vapid t=", authorization, StringComparison.Ordinal);
        var parts = authorization["vapid ".Length..].Split(", ").Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        Assert.Equal(publicKey, parts["k"]);
        var claims = VerifyJwt(parts["t"], WebEncoders.Base64UrlDecode(publicKey));
        Assert.Equal($"http://127.0.0.1:{service.Port}", claims.GetProperty("aud").GetString());
        Assert.Equal("https://ardea.test", claims.GetProperty("sub").GetString());
        var expires = DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("exp").GetInt64());
        Assert.InRange(expires, host.Time.GetUtcNow(), host.Time.GetUtcNow().AddHours(24));

        var payload = JsonDocument.Parse(device.Decrypt(request.Body)).RootElement;
        Assert.Equal(8030, payload.GetProperty("web_push").GetInt32());
        var notification = payload.GetProperty("notification");
        Assert.StartsWith("[Critical] #2", notification.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal("https://ardea.test/o/acme/reminders/2", notification.GetProperty("navigate").GetString());
        Assert.DoesNotContain("Critical", Encoding.UTF8.GetString(request.Body), StringComparison.Ordinal);
        Assert.Equal("sent", await host.ScalarAsync<string>("SELECT state FROM notify.delivery WHERE channel = 'push'"));
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.push_subscription WHERE last_success_at IS NOT NULL"));

        service.Answers.Enqueue(410);
        await s.EventAsync("action.raised", await s.ActionAsync(3, level: 4), """{"level":4}""");
        await s.EvaluateAsync();
        await s.DeliverAsync();
        Assert.Equal(2, service.Requests.Count);
        Assert.Equal("normal", service.Requests.Last().Headers["Urgency"]);
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.push_subscription"));
        Assert.Equal("dead", await host.ScalarAsync<string>("SELECT state FROM notify.delivery WHERE channel = 'push' ORDER BY created_at DESC LIMIT 1"));
        Assert.DoesNotContain(host.Logs.Messages, m => m.Contains("device-1", StringComparison.Ordinal));

        await s.EventAsync("action.raised", await s.ActionAsync(4, level: 4), """{"level":4}""");
        await s.EvaluateAsync();
        Assert.Equal(2L, await s.CountAsync("SELECT count(*) FROM notify.delivery WHERE channel = 'push'"));
    }

    [Fact]
    public async Task A_push_endpoint_that_resolves_to_an_internal_address_is_refused_and_removed()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await Device.Create().InsertAsync(host, await s.OwnerIdAsync(), "https://127.0.0.1:9/push/planted");

        await s.RuleAsync("Phones", 2, ["action.raised"], Scenario.Target("push"));
        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 4), """{"level":4}""");
        await s.EvaluateAsync();
        await s.DeliverAsync();

        Assert.Equal("dead", await host.ScalarAsync<string>("SELECT state FROM notify.delivery WHERE channel = 'push'"));
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.push_subscription"));
    }

    [Fact]
    public async Task Subscriptions_are_taken_only_for_the_browser_makers_push_services_and_only_their_owner_reaches_them()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host, keepDefaultRules: true);
        var keys = Device.Create().Keys;
        const string Endpoint = "https://fcm.googleapis.com/fcm/send/abc:APA91b-device";

        foreach (var (endpoint, p256dh, auth) in new[]
        {
            ("http://fcm.googleapis.com/fcm/send/abc", keys.P256dh, keys.Auth),
            ("https://push.attacker.example/abc", keys.P256dh, keys.Auth),
            (Endpoint, "not-a-key", keys.Auth),
            (Endpoint, keys.P256dh, "c2hvcnQ"),
        })
        {
            var refused = await s.Owner.PostAsync("/api/v1/me/push-subscriptions", new { endpoint, keys = new { p256dh, auth } });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        var created = await NotifyTestHost.JsonAsync(
            await s.Owner.PostAsync("/api/v1/me/push-subscriptions", new { endpoint = Endpoint, keys = new { p256dh = keys.P256dh, auth = keys.Auth } }),
            HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        await NotifyTestHost.JsonAsync(await s.Owner.PostAsync("/api/v1/me/push-subscriptions", new { endpoint = Endpoint, keys = new { p256dh = keys.P256dh, auth = keys.Auth } }));
        Assert.Single((await NotifyTestHost.JsonAsync(await s.Owner.GetAsync("/api/v1/me/push-subscriptions"))).EnumerateArray());

        var critical = (await NotifyTestHost.JsonAsync(await s.Owner.GetAsync($"{s.Path}/rules"))).EnumerateArray().Single(r => r.GetProperty("preset").GetString() == "critical");
        Assert.Contains(critical.GetProperty("targets").EnumerateArray(), t => t.GetProperty("channel").GetString() == "push" && t.GetProperty("recipientRef").GetString() == "admin");

        var other = await host.RegisterAsync("other@example.org", "Other");
        Assert.Empty((await NotifyTestHost.JsonAsync(await other.GetAsync("/api/v1/me/push-subscriptions"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.SendAsync(HttpMethod.Delete, $"/api/v1/me/push-subscriptions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/v1/me/push-subscriptions/{id}/test")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.SendAsync(HttpMethod.Delete, $"/api/v1/me/push-subscriptions?endpoint={Uri.EscapeDataString(Endpoint)}")).StatusCode);
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.push_subscription"));

        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.SendAsync(HttpMethod.Delete, $"/api/v1/me/push-subscriptions?endpoint={Uri.EscapeDataString(Endpoint)}")).StatusCode);
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.push_subscription"));
    }

    [Fact]
    public async Task Devices_are_reached_together_within_a_budget_and_a_gone_one_is_removed_although_the_message_is_retried()
    {
        await using var service = await FakePushService.StartAsync();
        await using var host = await NotifyTestHost.StartAsync(server, services: s =>
        {
            s.AddHttpClient(WebPushChannel.ClientName).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false });
            s.AddSingleton(new PushOptions { Budget = TimeSpan.FromSeconds(2) });
        });
        var s = await Scenario.StartAsync(host);
        var owner = await s.OwnerIdAsync();
        foreach (var name in new[] { "slow-1", "slow-2", "slow-3", "gone" })
        {
            await Device.Create().InsertAsync(host, owner, $"http://127.0.0.1:{service.Port}/push/{name}");
        }

        await s.RuleAsync("Phones", 2, ["action.raised"], Scenario.Target("push"));
        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 5), """{"level":5}""");
        await s.EvaluateAsync();
        host.Time.Advance(TimeSpan.FromMinutes(3));
        var started = System.Diagnostics.Stopwatch.StartNew();
        var errors = await host.RunJobsAsync();

        Assert.InRange(started.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(15));
        Assert.Single(errors);
        Assert.Equal(4, service.Requests.Count);
        Assert.Equal("failed", await host.ScalarAsync<string>("SELECT state FROM notify.delivery WHERE channel = 'push'"));
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.push_subscription WHERE endpoint LIKE '%/gone'"));
        Assert.Equal(3L, await s.CountAsync("SELECT count(*) FROM notify.push_subscription WHERE failed_count = 1"));
    }

    [Fact]
    public async Task Three_refusals_of_the_key_in_a_row_remove_a_device_and_other_failures_break_the_run()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await Device.Create().InsertAsync(host, await s.OwnerIdAsync(), "https://fcm.googleapis.com/fcm/send/refused");
        var id = await host.ScalarAsync<Guid>("SELECT id FROM notify.push_subscription");

        async Task RecordAsync(PushDeviceOutcome outcome, string status)
        {
            await using var scope = host.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetOrganization(s.Organization);
            await scope.ServiceProvider.GetRequiredService<WebPushChannel>()
                .RecordAsync(scope.ServiceProvider.GetRequiredService<Persistence.NotifyDbContext>(), [new PushDeviceResult(id, outcome, status)], CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
        }

        await RecordAsync(PushDeviceOutcome.Rejected, "403");
        await RecordAsync(PushDeviceOutcome.Unavailable, "503");
        await RecordAsync(PushDeviceOutcome.Rejected, "403");
        await RecordAsync(PushDeviceOutcome.Rejected, "401");
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.push_subscription"));
        await RecordAsync(PushDeviceOutcome.Rejected, "403");
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.push_subscription"));
    }

    [Fact]
    public void A_message_in_Cyrillic_or_Chinese_fits_the_four_kilobytes_of_a_push_message()
    {
        var title = string.Concat(Enumerable.Repeat("Критическая уязвимость 严重漏洞 ", 20));
        var body = string.Concat(Enumerable.Repeat("Обновите до версии 2025.12.1 并重启服务器。", 80));
        var json = Messages.MessageRenderer.PushPayload("ru", Guid.CreateVersion7(), title, body, new Uri("https://ardea.test/o/acme/actions/143"), 5, "acme");

        Assert.InRange(Encoding.UTF8.GetByteCount(json), 1, WebPushEncryption.MaxPlaintext);
        Assert.Contains("Критическая", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", json, StringComparison.Ordinal);
        var notification = JsonDocument.Parse(json).RootElement.GetProperty("notification");
        Assert.EndsWith("…", notification.GetProperty("body").GetString(), StringComparison.Ordinal);
        Assert.Equal("https://ardea.test/o/acme/actions/143", notification.GetProperty("navigate").GetString());
    }

    [Fact]
    public async Task The_same_device_subscribing_twice_at_once_ends_with_one_subscription()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        var keys = Device.Create().Keys;
        var body = new { endpoint = "https://fcm.googleapis.com/fcm/send/twice", keys = new { p256dh = keys.P256dh, auth = keys.Auth } };

        var answers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => s.Owner.PostAsync("/api/v1/me/push-subscriptions", body)));

        Assert.All(answers, a => Assert.True(a.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, a.StatusCode.ToString()));
        Assert.Single(answers, a => a.StatusCode == HttpStatusCode.Created);
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.push_subscription"));
    }

    [Fact]
    public async Task A_device_is_seen_only_by_its_user_and_by_jobs_of_an_organisation_the_user_belongs_to()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var a = await Scenario.StartAsync(host);
        using var member = await host.RegisterAsync("member@example.org", "Member");
        await host.AddMemberAsync(a.Organization, "member@example.org", "member");
        using var other = await host.RegisterAsync("other@example.org", "Other");
        var b = await NotifyTestHost.OrganizationAsync(other, "Beta");
        var keys = Device.Create().Keys;
        await NotifyTestHost.JsonAsync(
            await member.PostAsync("/api/v1/me/push-subscriptions", new { endpoint = "https://fcm.googleapis.com/fcm/send/member", keys = new { p256dh = keys.P256dh, auth = keys.Auth } }),
            HttpStatusCode.Created);
        var otherId = await host.ScalarAsync<Guid>("SELECT id FROM iam.\"user\" WHERE normalized_email = 'OTHER@EXAMPLE.ORG'");

        async Task<(int Seen, int Deleted)> AsAsync(Guid organization, Guid? user)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
            tenant.SetOrganization(organization);
            if (user is { } u)
            {
                tenant.SetUser(u);
            }

            var db = scope.ServiceProvider.GetRequiredService<Persistence.NotifyDbContext>();
            var seen = await db.PushSubscriptions.CountAsync();
            var deleted = await db.PushSubscriptions.ExecuteDeleteAsync();
            return (seen, deleted);
        }

        Assert.Equal((0, 0), await AsAsync(b, null));
        Assert.Equal((0, 0), await AsAsync(b, otherId));
        Assert.Empty((await NotifyTestHost.JsonAsync(await other.GetAsync("/api/v1/me/push-subscriptions"))).EnumerateArray());
        Assert.Equal(1, (await AsAsync(a.Organization, null)).Seen);
        Assert.Equal(1L, await a.CountAsync("SELECT count(*) FROM notify.push_subscription"));
    }

    [Fact]
    public void Encryption_matches_the_example_of_RFC_8291()
    {
        var plaintext = WebEncoders.Base64UrlDecode("V2hlbiBJIGdyb3cgdXAsIEkgd2FudCB0byBiZSBhIHdhdGVybWVsb24");
        using var server = ECDiffieHellman.Create(P256.Parameters(
            WebEncoders.Base64UrlDecode("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8"),
            WebEncoders.Base64UrlDecode("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw")));
        var body = WebPushEncryption.Encrypt(
            plaintext,
            WebEncoders.Base64UrlDecode("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4"),
            WebEncoders.Base64UrlDecode("BTBZMqHH6r4Tts7J_aSIgg"),
            server,
            WebEncoders.Base64UrlDecode("DGv6ra1nlYgDCS1FRnbzlw"));
        Assert.Equal(
            "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN",
            WebEncoders.Base64UrlEncode(body));
    }

    private static JsonElement VerifyJwt(string token, byte[] publicKey)
    {
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        using var key = ECDsa.Create(P256.Parameters(publicKey, null));
        Assert.True(
            key.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), WebEncoders.Base64UrlDecode(parts[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            "The VAPID token's signature does not verify with the instance's public key.");
        Assert.Equal("ES256", JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[0])).RootElement.GetProperty("alg").GetString());
        return JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[1])).RootElement.Clone();
    }

    private sealed class Device
    {
        private readonly ECDiffieHellman _key;
        private readonly byte[] _auth;

        private Device(ECDiffieHellman key, byte[] auth)
        {
            _key = key;
            _auth = auth;
        }

        public (string P256dh, string Auth) Keys => (WebEncoders.Base64UrlEncode(WebPushEncryption.PublicKey(_key)), WebEncoders.Base64UrlEncode(_auth));

        public static Device Create() => new(ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256), RandomNumberGenerator.GetBytes(16));

        public Task InsertAsync(NotifyTestHost host, Guid user, string endpoint) => host.ExecuteAsync($"""
            INSERT INTO notify.push_subscription (user_id, endpoint, endpoint_hash, p256dh, auth, label, created_at, failed_count, rejected_count)
            VALUES ('{user}', '{endpoint}', '{WebPushChannel.Hash(endpoint)}', '{Keys.P256dh}', '{Keys.Auth}', 'Test browser', now(), 0, 0)
            """);

        public string Decrypt(byte[] body)
        {
            var salt = body[..16];
            Assert.Equal(4096u, BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16, 4)));
            var idLength = body[20];
            var serverPublic = body[21..(21 + idLength)];
            var ciphertext = body[(21 + idLength)..];
            using var server = P256.ImportPublic(serverPublic);
            var shared = _key.DeriveRawSecretAgreement(server.PublicKey);
            var info = Encoding.ASCII.GetBytes("WebPush: info\0").Concat(WebPushEncryption.PublicKey(_key)).Concat(serverPublic).ToArray();
            var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, _auth, info);
            var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
            var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
            var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));
            var plain = new byte[ciphertext.Length - 16];
            using (var aes = new AesGcm(cek, 16))
            {
                aes.Decrypt(nonce, ciphertext[..^16], ciphertext[^16..], plain);
            }

            var end = Array.LastIndexOf(plain, (byte)2);
            Assert.True(end >= 0, "The last record has no delimiter.");
            return Encoding.UTF8.GetString(plain, 0, end);
        }
    }

    private sealed record Received(string Path, Dictionary<string, string> Headers, byte[] Body);

    private sealed class FakePushService : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private FakePushService(WebApplication app, int port)
        {
            _app = app;
            Port = port;
        }

        public int Port { get; }

        public ConcurrentQueue<Received> Requests { get; } = new();

        public ConcurrentQueue<int> Answers { get; } = new();

        public static async Task<FakePushService> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(k => k.Listen(IPAddress.Loopback, 0, o => o.Protocols = HttpProtocols.Http1));
            var app = builder.Build();
            FakePushService? self = null;
            app.MapPost("/{**path}", async (HttpRequest request) =>
            {
                using var body = new MemoryStream();
                await request.Body.CopyToAsync(body);
                var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                self!.Requests.Enqueue(new Received(request.Path, headers, body.ToArray()));
                if (request.Path.Value!.Contains("/slow", StringComparison.Ordinal))
                {
                    await Task.Delay(Timeout.Infinite, request.HttpContext.RequestAborted).ContinueWith(_ => { }, TaskScheduler.Default);
                    return Results.StatusCode(504);
                }

                return Results.StatusCode(request.Path.Value!.EndsWith("/gone", StringComparison.Ordinal) ? 410 : self.Answers.TryDequeue(out var status) ? status : 201);
            });
            await app.StartAsync();
            self = new FakePushService(app, new Uri(app.Urls.First()).Port);
            return self;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
