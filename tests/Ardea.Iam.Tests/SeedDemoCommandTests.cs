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
    public async Task Seeds_an_empty_instance_in_well_under_two_minutes_signs_in_and_refuses_a_second_run()
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
        }).Build();
        await using var services = CommandServices.Build(configuration, "seed-demo", Microsoft.Extensions.Logging.LogLevel.Warning);

        var output = new StringWriter();
        var watch = Stopwatch.StartNew();
        Assert.Equal(0, await SeedDemoCommand.SeedAsync(services, output, TextWriter.Null, TestContext.Current.CancellationToken));
        var elapsed = watch.Elapsed;
        TestContext.Current.SendDiagnosticMessage($"seed-demo took {elapsed.TotalSeconds:F1} s{Environment.NewLine}{output}");
        Assert.True(elapsed < TimeSpan.FromMinutes(2), $"seed-demo took {elapsed}");

        // The sample data of the mockups: one organisation, three people, three projects, and what the pipeline made of the records.
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.organization"));
        Assert.Equal(3L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.membership"));
        Assert.Equal(3L, await host.ScalarAsync<long>("SELECT count(*) FROM assets.project"));
        Assert.Equal(11L, await host.ScalarAsync<long>("SELECT count(*) FROM assets.component"));
        Assert.True(await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry") >= 10);
        Assert.True(await host.ScalarAsync<long>("SELECT count(*) FROM assess.finding WHERE level >= 3") >= 10);
        Assert.True(await host.ScalarAsync<long>("SELECT count(*) FROM assess.remediation WHERE status <> 'closed'") is >= 4 and <= 8);
        Assert.True(await host.ScalarAsync<long>("SELECT count(*) FROM assess.assess_event") >= 4);
        // The people's work is in the history: one action in review with its assignee, one closed.
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.remediation WHERE status = 'in_review' AND assigned_to IS NOT NULL"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.remediation WHERE status = 'closed'"));
        Assert.True(await host.ScalarAsync<long>("SELECT count(*) FROM assess.assess_event WHERE kind IN ('action.status_changed', 'action.assigned', 'action.note_added')") >= 4);
        Assert.True(await host.ScalarAsync<long>("SELECT count(*) FROM notify.delivery WHERE channel = 'in_app'") >= 2);
        // Nothing that reaches out is left switched on or queued: no fetch, no e-mail, no repository reading.
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.source WHERE enabled"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE state = 'pending' AND kind LIKE ANY (ARRAY['intel.sync%', 'assets.index%', 'notify.deliver', 'iam.%'])"));

        // The printed credentials sign in, and the organisation shows the actions.
        var password = output.ToString().Split("password  ")[1].Split(Environment.NewLine)[0].Trim();
        using var client = host.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/login", new { email = "nina.meier@example.org", password })).StatusCode);
        var session = await (await client.SessionAsync()).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var organization = session.GetProperty("organizations")[0];
        Assert.Equal("example-digital", organization.GetProperty("slug").GetString());
        var path = $"/api/v1/orgs/{organization.GetProperty("id").GetGuid()}";
        var actions = await (await client.GetAsync($"{path}/actions")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.InRange(actions.GetProperty("items").GetArrayLength(), 4, 8);
        var unread = await (await client.GetAsync($"{path}/notifications/unread-count")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(unread.GetProperty("unread").GetInt32() >= 1);

        // A database with data is refused, and nothing changes.
        var before = await host.ScalarAsync<long>("SELECT (SELECT count(*) FROM assess.finding) + (SELECT count(*) FROM platform.job) + (SELECT count(*) FROM platform.audit_event)");
        var error = new StringWriter();
        Assert.Equal(1, await SeedDemoCommand.SeedAsync(services, TextWriter.Null, error, TestContext.Current.CancellationToken));
        Assert.Contains("holds data", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, await host.ScalarAsync<long>("SELECT (SELECT count(*) FROM assess.finding) + (SELECT count(*) FROM platform.job) + (SELECT count(*) FROM platform.audit_event)"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.organization"));
    }
}
