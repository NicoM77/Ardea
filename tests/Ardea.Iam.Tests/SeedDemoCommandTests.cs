using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Ardea.Host.Commands;
using Ardea.TestSupport;
using Microsoft.Extensions.Configuration;

namespace Ardea.Iam.Tests;

/// <summary><c>ardea seed-demo</c> (task 7.6): the sample data of the mockups in an empty instance, through the real pipeline.</summary>
public sealed class SeedDemoCommandTests(PostgresServer server)
{
    [Fact]
    public async Task Seeds_an_empty_instance_in_under_two_minutes_signs_in_and_refuses_a_second_run_without_a_change()
    {
        var masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var host = await ApiTestHost.StartAsync(server, new Dictionary<string, string?> { ["MASTER_KEY"] = masterKey });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_HOST"] = host.Database.Host,
            ["DB_PORT"] = host.Database.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["DB_NAME"] = host.Database.Database,
            ["DB_OWNER_USER"] = host.Database.OwnerUser,
            ["DB_OWNER_PASSWORD"] = host.Database.OwnerPassword,
            ["DB_APP_PASSWORD"] = host.Database.AppPassword,
            ["MASTER_KEY"] = masterKey,
            ["PUBLIC_URL"] = "https://localhost",
            ["PASSWORD_HASH_ITERATIONS"] = "1000",
            // With SMTP the rules would e-mail the demo's invented addresses; the command must leave nothing to send.
            ["SMTP:HOST"] = "smtp.invalid",
            ["SMTP:FROM"] = "ardea@example.org",
        }).Build();
        await using var services = CommandServices.Build(configuration, "seed-demo", Microsoft.Extensions.Logging.LogLevel.Warning);

        var output = new StringWriter();
        var watch = Stopwatch.StartNew();
        Assert.Equal(0, await SeedDemoCommand.SeedAsync(services, output, TextWriter.Null, TestContext.Current.CancellationToken));
        var elapsed = watch.Elapsed;
        TestContext.Current.SendDiagnosticMessage($"seed-demo took {elapsed.TotalSeconds:F1} s");
        Assert.True(elapsed < TimeSpan.FromMinutes(2), $"seed-demo took {elapsed}");

        // The sample data of the mockups: one organisation, three people, three projects, and what the pipeline made of the records.
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.organization WHERE settings -> 'setup' ->> 'completedAt' IS NOT NULL"));
        Assert.Equal(3L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.membership"));
        Assert.Equal(3L, await host.ScalarAsync<long>("SELECT count(*) FROM assets.project"));
        Assert.Equal(11L, await host.ScalarAsync<long>("SELECT count(*) FROM assets.component"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM assets.component WHERE product_key IS NOT NULL AND version_scheme IS NULL"));
        Assert.Equal(16L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry"));
        Assert.True(await host.ScalarAsync<long>("SELECT count(*) FROM assess.finding WHERE level >= 3") >= 10);

        // Open and done only: phase 5a drops assignment, deadline set by hand and «in review», so the demo uses none of them.
        Assert.InRange(await host.ScalarAsync<long>("SELECT count(*) FROM assess.remediation WHERE status = 'open'"), 3, 6);
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.remediation WHERE status = 'in_review' OR assigned_to IS NOT NULL OR due_at_manual"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.finding WHERE status = 'in_review'"));
        // Done once by a person, once by the pipeline after the profile follows the .NET update.
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.remediation WHERE status = 'closed' AND close_reason = 'not_affected'"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.remediation WHERE status = 'closed' AND close_reason = 'fixed_by_update'"));
        Assert.Equal(2L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assess_event WHERE kind = 'action.note_added'"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assess_event WHERE kind = 'action.status_changed'"));
        Assert.True(await host.ScalarAsync<long>("SELECT count(*) FROM notify.notification") >= 2);

        // Nothing that reaches out is left switched on or queued: no fetch, no e-mail, no repository reading.
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.source WHERE enabled"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM notify.delivery WHERE channel <> 'in_app' AND state IN ('pending', 'sending', 'failed')"));
        Assert.True(await host.ScalarAsync<long>("SELECT count(*) FROM notify.delivery WHERE channel = 'email' AND state = 'suppressed'") >= 1);
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE state = 'pending' AND kind LIKE ANY (ARRAY['intel.sync%', 'intel.check%', 'assets.index%', 'iam.%'])"));

        // The printed credentials sign in, and the organisation shows its actions.
        var password = output.ToString().Split("password  ")[1].Split(Environment.NewLine)[0].Trim();
        using var client = host.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/login", new { email = "nina.meier@example.org", password })).StatusCode);
        var session = await (await client.SessionAsync()).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var organization = session.GetProperty("organizations")[0];
        Assert.Equal("example-digital", organization.GetProperty("slug").GetString());
        var path = $"/api/v1/orgs/{organization.GetProperty("id").GetGuid()}";
        var actions = await (await client.GetAsync($"{path}/actions?status=all")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.InRange(actions.GetProperty("items").GetArrayLength(), 5, 8);
        var unread = await (await client.GetAsync($"{path}/notifications/unread-count")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(unread.GetProperty("unread").GetInt32() >= 1);

        // A database with data is refused, and nothing changes.
        const string Fingerprint = "SELECT (SELECT count(*) FROM assess.finding) + (SELECT count(*) FROM platform.job) + (SELECT count(*) FROM platform.audit_event) + (SELECT count(*) FROM iam.\"user\")";
        var before = await host.ScalarAsync<long>(Fingerprint);
        var error = new StringWriter();
        Assert.Equal(1, await SeedDemoCommand.SeedAsync(services, TextWriter.Null, error, TestContext.Current.CancellationToken));
        Assert.Contains("holds data", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, await host.ScalarAsync<long>(Fingerprint));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.organization"));
    }
}
