using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.Host.Features;
using Ardea.Platform.Email;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Iam.Tests;

public sealed class InstanceStatusTests(PostgresServer server)
{
    [Fact]
    public async Task A_stalled_worker_and_a_stale_source_are_warned_about_and_only_the_administrator_sees_the_figures()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var admin = host.Client();
        await admin.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "admin@example.org", displayName = "Admin", password = ApiTestHost.Password });
        using var member = host.Client();
        await member.PostAsync("/api/v1/auth/register", new { email = "member@example.org", displayName = "Member", password = ApiTestHost.Password });
        await host.ScalarAsync<int>("""
            INSERT INTO intel.source (key, name, kind, operator, operator_kind, adapter, enabled, interval, config, attribution, terms_mode, parser_version, next_sync_at, consecutive_failures, health, created_at)
            VALUES ('cve-program', 'CVE Program', 'record', 'CVE', 'mandated', 'x', true, interval '1 hour', '{}', 'CVE', 'full', 1, now(), 3, 'failing', now() - interval '7 hours'),
                   ('fresh', 'Fresh source', 'record', 'X', 'community', 'x', true, interval '1 hour', '{}', 'X', 'full', 1, now(), 0, 'healthy', now() - interval '1 hour')
            RETURNING 1
            """);
        await host.ScalarAsync<int>("INSERT INTO platform.job (kind, payload, run_at, priority, state, attempts, max_attempts, timeout_seconds) VALUES ('intel.correlate-entry', '{}', now() - interval '5 minutes', 2, 'pending', 0, 5, 60) RETURNING 1");

        var status = await (await admin.GetAsync("/api/v1/instance/status")).Content.ReadFromJsonAsync<JsonElement>();
        var warnings = status.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString() + ":" + (w.TryGetProperty("subject", out var s) ? s.GetString() : null)).ToList();
        Assert.Contains("worker_stalled:", warnings);
        Assert.Contains("source_stale:cve-program", warnings);
        Assert.DoesNotContain("source_stale:fresh", warnings);
        var metrics = status.GetProperty("metrics").EnumerateArray().ToDictionary(m => m.GetProperty("group").GetString() + "/" + m.GetProperty("name").GetString(), m => m.GetProperty("value").GetDouble());
        Assert.Equal(3, metrics["sources/cve-program.failures"]);
        Assert.Equal(1, metrics["jobs/intel.correlate-entry.pending"]);
        Assert.True(metrics["jobs/intel.correlate-entry.oldest_wait"] >= 290);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/instance/status")).StatusCode);
        var userWarnings = await (await member.GetAsync("/api/v1/status")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(userWarnings.EnumerateArray(), w => w.GetProperty("code").GetString() == "source_stale");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client().GetAsync("/api/v1/status")).StatusCode);
    }

    [Fact]
    public async Task The_administration_shows_when_the_administrators_were_mailed_about_a_warning()
    {
        var mail = new AcceptingEmailSender();
        await using var host = await ApiTestHost.StartAsync(server, services: s => s.AddScoped<IEmailSender>(_ => mail));
        using var admin = host.Client();
        await admin.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "admin@example.org", displayName = "Admin", password = ApiTestHost.Password });

        var before = await (await admin.GetAsync("/api/v1/instance/status")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(before.GetProperty("notified").EnumerateArray());

        Assert.True(await host.Services.GetRequiredService<InstanceSelfCheck>().RunRoundAsync(TestContext.Current.CancellationToken));

        var status = await (await admin.GetAsync("/api/v1/instance/status")).Content.ReadFromJsonAsync<JsonElement>();
        var notified = Assert.Single(status.GetProperty("notified").EnumerateArray());
        Assert.Equal("worker_stalled", notified.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, notified.GetProperty("subject").ValueKind);
        Assert.True(notified.GetProperty("at").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal("admin@example.org", Assert.Single(mail.Sent).To);
    }

    private sealed class AcceptingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            lock (Sent)
            {
                Sent.Add(message);
            }

            return Task.CompletedTask;
        }
    }
}
