using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Ardea.Host.Commands;
using Ardea.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ardea.Iam.Tests;

/// <summary><c>ardea seed-demo</c> (task 7.6): the sample data of the mockups in an empty instance, through the real pipeline.</summary>
public sealed class SeedDemoCommandTests(PostgresServer server)
{
    /// <summary>One row in a table that marks a used instance; each must refuse the command on its own.</summary>
    public static TheoryData<string, string> UsedInstances => new()
    {
        { "iam.\"user\"", """INSERT INTO iam."user" (display_name, is_instance_admin, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count) VALUES ('Someone', false, false, false, false, false, 0)""" },
        { "iam.organization", "INSERT INTO iam.organization (id, name, slug, locale, time_zone, settings, created_at) VALUES (uuidv7(), 'Acme', 'acme', 'en', 'UTC', '{}', now())" },
        { "assets.project", "INSERT INTO assets.project (organization_id, name, criticality, exposure, created_at, changed_at) VALUES (uuidv7(), 'Shop', 'normal', 'internal', now(), now())" },
        {
            "intel.source_record",
            """
            WITH s AS (INSERT INTO intel.source (key, name, kind, operator, operator_kind, adapter, enabled, interval, config, attribution, terms_mode, parser_version, next_sync_at, consecutive_failures, health)
                       VALUES ('test', 'Test', 'record', 'Test', 'community', 'test', false, '1 hour', '{}', 'Test', 'full', 1, now(), 0, 'disabled') RETURNING id)
            INSERT INTO intel.source_record (source_id, external_id, content_type, content_hash, payload, revision, first_seen_at, last_seen_at, parse_status)
            SELECT id, 'X-1', 'application/json', 'h', '{}', 1, now(), now(), 'pending' FROM s
            """
        },
        {
            "intel.security_entry",
            """
            INSERT INTO intel.security_entry (kind, primary_key, state, changed_at, revision, material_fingerprint, remediation_fingerprint, exploitation, fix_available, cwe_ids, authority_warning, provenance)
            VALUES ('vulnerability', 'CVE-2026-0001', 'published', now(), 1, 'm', 'r', 'none', false, '{}', false, '{}')
            """
        },
        // A worker that started first: it registers and may queue a source sync before any record is stored.
        { "platform.worker_heartbeat", "INSERT INTO platform.worker_heartbeat (worker_id, started_at, beat_at) VALUES ('worker-1', now(), now())" },
        { "platform.job", "INSERT INTO platform.job (kind, payload, run_at, priority, state, attempts, max_attempts, timeout_seconds) VALUES ('intel.sync-source', '{}', now(), 0, 'pending', 0, 1, 60)" },
    };

    [Fact]
    public async Task Seeds_an_empty_instance_in_under_two_minutes_signs_in_and_refuses_a_second_run_without_a_change()
    {
        var masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var host = await ApiTestHost.StartAsync(server, new Dictionary<string, string?> { ["MASTER_KEY"] = masterKey });
        await using var services = CommandServicesFor(host, masterKey);

        var output = new StringWriter();
        var watch = Stopwatch.StartNew();
        Assert.Equal(0, await SeedDemoCommand.SeedAsync(services, output, TextWriter.Null, TestContext.Current.CancellationToken));
        var elapsed = watch.Elapsed;
        TestContext.Current.SendDiagnosticMessage($"seed-demo took {elapsed.TotalSeconds:F1} s");
        Assert.True(elapsed < TimeSpan.FromMinutes(2), $"seed-demo took {elapsed}");

        // The sample data of the mockups: one organisation, three people, three projects, and what the pipeline made of the records.
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.organization WHERE settings -> 'setup' ->> 'completedAt' IS NOT NULL"));
        Assert.Equal(3L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.membership"));
        Assert.Equal(3L, await host.ScalarAsync<long>("SELECT count(*) FROM assets.project WHERE kind = 'project'"));
        Assert.Equal(11L, await host.ScalarAsync<long>("SELECT count(*) FROM assets.component c JOIN assets.project p ON p.id = c.project_id WHERE p.kind = 'project'"));

        // The organisation has its stack from its creation on (master plan 17.2) with eight broad entries as the wizard adds
        // them (task 5a.4), two with a version line and the firewall with every entry, and four the demo's records hit (task
        // 5a.3); the projects' versions are releases, and the managed Azure SQL has none (ADR 046).
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assets.project WHERE kind = 'organization_stack'"));
        Assert.Equal("exact 10, none 1", await host.ScalarAsync<string>(
            "SELECT string_agg(version_precision || ' ' || n, ', ' ORDER BY version_precision) FROM (SELECT version_precision, count(*) AS n FROM assets.component c JOIN assets.project p ON p.id = c.project_id WHERE p.kind = 'project' GROUP BY 1) p"));
        Assert.Equal("line 3, none 9", await host.ScalarAsync<string>(
            "SELECT string_agg(version_precision || ' ' || n, ', ' ORDER BY version_precision) FROM (SELECT version_precision, count(*) AS n FROM assets.component c JOIN assets.project p ON p.id = c.project_id WHERE p.kind = 'organization_stack' GROUP BY 1) p"));
        Assert.Equal(2L, await host.ScalarAsync<long>("SELECT count(*) FROM assets.component WHERE notice_threshold = 'everything'"));

        // The stack's notices (task 5a.3): raised by the pipeline, one answered each way, the rest new for the Notices page.
        Assert.Equal("affects_us 1, not_affected 1, open 5", await host.ScalarAsync<string>(
            "SELECT string_agg(state || ' ' || n, ', ' ORDER BY state) FROM (SELECT state, count(*) AS n FROM assess.notice GROUP BY 1) s"));
        Assert.Equal(1L, await host.ScalarAsync<long>(
            "SELECT count(*) FROM assess.notice n JOIN assess.remediation r ON r.id = n.remediation_id JOIN assets.project p ON p.id = r.project_id WHERE p.kind = 'organization_stack' AND r.status = 'open'"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM assets.component WHERE product_key IS NOT NULL AND version_scheme IS NULL"));
        Assert.Equal(16L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry"));
        Assert.True(await host.ScalarAsync<long>("SELECT count(*) FROM assess.finding WHERE level >= 3") >= 10);

        // Open and done only (master plan 17.5); the retired columns of assignment and deadline stay unwritten (ADR 046).
        Assert.InRange(await host.ScalarAsync<long>("SELECT count(*) FROM assess.remediation WHERE status = 'open'"), 3, 6);
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.remediation WHERE assigned_to IS NOT NULL OR due_at IS NOT NULL OR due_at_manual"));
        // Done once by a person, once by the pipeline after the profile follows the .NET update.
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.remediation WHERE status = 'closed' AND close_reason = 'not_affected'"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.remediation WHERE status = 'closed' AND close_reason = 'fixed_by_update'"));
        Assert.Equal(2L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assess_event WHERE kind = 'action.note_added'"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assess_event WHERE kind = 'action.status_changed'"));
        Assert.True(await host.ScalarAsync<long>("SELECT count(*) FROM notify.notification") >= 2);
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.audit_event WHERE origin <> 'command:seed-demo'"));

        // Nothing that reaches out is left switched on or queued: no fetch, no repository reading, no language model,
        // and with SMTP configured no e-mail or push to the invented people, neither now nor in a later digest or reminder run
        // (the reminders by level reach only the targets of enabled rules, ADR 048).
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.source WHERE enabled"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM notify.rule_target t JOIN notify.rule r ON r.id = t.rule_id WHERE r.enabled AND t.channel <> 'in_app'"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM notify.digest_item"));
        Assert.True(await host.ScalarAsync<long>("SELECT count(*) FROM notify.rule WHERE enabled") >= 2);
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM notify.delivery WHERE channel <> 'in_app' AND state IN ('pending', 'sending', 'failed')"));
        Assert.True(await host.ScalarAsync<long>("SELECT count(*) FROM notify.delivery WHERE channel = 'email' AND state = 'suppressed'") >= 1);
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.organization WHERE settings ->> 'aiPolicy' = 'none'"));
        Assert.Equal(0L, await host.ScalarAsync<long>(
            "SELECT count(*) FROM platform.job WHERE state = 'pending' AND kind LIKE ANY (ARRAY['intel.sync%', 'intel.check%', 'assets.index%', 'assess.estimate%', 'iam.%'])"));

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
        var before = await host.ScalarAsync<long>(Fingerprint);
        var error = new StringWriter();
        Assert.Equal(1, await SeedDemoCommand.SeedAsync(services, TextWriter.Null, error, TestContext.Current.CancellationToken));
        Assert.Contains("not empty", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, await host.ScalarAsync<long>(Fingerprint));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.organization"));
    }

    [Theory]
    [MemberData(nameof(UsedInstances))]
    public async Task One_row_of_a_used_instance_refuses_the_seed_and_changes_nothing(string table, string insert)
    {
        var masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var host = await ApiTestHost.StartAsync(server, new Dictionary<string, string?> { ["MASTER_KEY"] = masterKey });
        await using var services = CommandServicesFor(host, masterKey);
        Assert.Equal(0L, await host.ScalarAsync<long>(Fingerprint));
        await using (var owner = new NpgsqlConnection(host.Database.OwnerConnectionString("tests")))
        {
            await owner.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand(insert, owner);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var before = await host.ScalarAsync<long>(Fingerprint);
        var error = new StringWriter();
        Assert.Equal(1, await SeedDemoCommand.SeedAsync(services, TextWriter.Null, error, TestContext.Current.CancellationToken));
        Assert.Contains("not empty", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(SeedDemoCommand.Occupancy.Single(o => o.Table == table).Noun, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, await host.ScalarAsync<long>(Fingerprint));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.membership"));
    }

    /// <summary>Rows in the tables any step of the seed writes to; equal before and after means nothing changed.</summary>
    private const string Fingerprint = """
        SELECT (SELECT count(*) FROM iam."user") + (SELECT count(*) FROM iam.organization) + (SELECT count(*) FROM assets.project)
             + (SELECT count(*) FROM intel.source) + (SELECT count(*) FROM intel.source_record) + (SELECT count(*) FROM intel.security_entry)
             + (SELECT count(*) FROM assess.finding) + (SELECT count(*) FROM notify.rule) + (SELECT count(*) FROM platform.job)
             + (SELECT count(*) FROM platform.audit_event)
        """;

    private static ServiceProvider CommandServicesFor(ApiTestHost host, string masterKey) => CommandServices.Build(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
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
            // With SMTP the default rules would e-mail the demo's invented addresses; the command must leave nothing to send.
            ["SMTP:HOST"] = "smtp.invalid",
            ["SMTP:FROM"] = "ardea@example.org",
        }).Build(),
        "seed-demo",
        Microsoft.Extensions.Logging.LogLevel.Warning);
}
