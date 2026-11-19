using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Iam.Tests;

/// <summary>The system warns about itself; the administration shows the operating figures (task 6.6; master plan 12.7).</summary>
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

        // Users see whether the data is current, not the figures.
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/instance/status")).StatusCode);
        var userWarnings = await (await member.GetAsync("/api/v1/status")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(userWarnings.EnumerateArray(), w => w.GetProperty("code").GetString() == "source_stale");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client().GetAsync("/api/v1/status")).StatusCode);
    }
}
