using Ardea.Platform.Persistence;
using Ardea.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Ardea.Host.Tests;

/// <summary>
/// Task 5a.2 (master plan 17.2, 17.5, 17.6 E25; ADR 046): an instance with organisations, components and actions from
/// before is migrated. Every organisation gets its stack, every component its precision, «in review» becomes open, and an
/// open action on a component without a version becomes notices and closes saying so; everything else stays.
/// </summary>
public sealed class StackNoticeMigrationTests(PostgresServer server)
{
    /// <summary>The migrations of tasks 5a.2 and 5a.3; the state before them is the old state.</summary>
    private static readonly string[] Added = ["_StackAndPrecision", "_NoticesAndOpenOrDone", "_WithoutAssignment", "_NoisyProducts", "_NoticeGeneration"];

    [Fact]
    public async Task Open_actions_without_a_version_become_notices_and_everything_else_stays()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = Options(await server.CreateDatabaseAsync("stacknotice"));
        await using var owner = new NpgsqlConnection(options.OwnerConnectionString("tests"));
        await owner.OpenAsync(ct);
        var migrator = new DatabaseMigrator(options, Modules.Databases, new NoDump(), NullLogger<DatabaseMigrator>.Instance);
        await migrator.EnsureAppRoleAsync(owner, ct);
        // Later migrations (e.g. task 5a.6) come after them: they stay unapplied too, and run with them.
        var pending = 0;
        foreach (var database in Modules.Databases)
        {
            await using var context = database.CreateContext(owner);
            var all = context.Database.GetMigrations().ToList();
            var before = all.TakeWhile(m => !Added.Any(a => m.EndsWith(a, StringComparison.Ordinal))).ToList();
            pending += all.Count - before.Count;
            if (before.LastOrDefault() is { } last)
            {
                await context.GetService<IMigrator>().MigrateAsync(last, ct);
            }

            if (owner.State != System.Data.ConnectionState.Open)
            {
                await owner.OpenAsync(ct);
            }
        }

        await ExecuteAsync(owner, """
            INSERT INTO iam.organization (id, name, slug, locale, time_zone, settings, created_at) VALUES
                ('01900000-0000-7000-8000-000000000001', 'Acme', 'acme', 'en', 'UTC', '{}', now()),
                ('01900000-0000-7000-8000-000000000002', 'Empty', 'empty', 'en', 'UTC', '{}', now());
            INSERT INTO assets.project (id, organization_id, name, criticality, exposure, created_at, changed_at) VALUES
                ('01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000001', 'Platform', 'normal', 'internal', now(), now());
            INSERT INTO assets.component (id, organization_id, project_id, product_key, custom_name, kind, version, packaging, protection, origin, purl, created_at, changed_at) VALUES
                ('01900000-0000-7000-8000-000000000021', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000010', 'postgresql', NULL, 'database', NULL, 'unknown', 'none', 'manual', NULL, now(), now()),
                ('01900000-0000-7000-8000-000000000022', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000010', NULL, 'Billing', 'other', NULL, 'unknown', 'none', 'manual', NULL, now(), now()),
                ('01900000-0000-7000-8000-000000000023', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000010', 'redis', NULL, 'database', '17.11', 'unknown', 'none', 'manual', NULL, now(), now()),
                ('01900000-0000-7000-8000-000000000024', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000010', 'php', NULL, 'runtime', '8.x', 'unknown', 'none', 'manual', NULL, now(), now()),
                ('01900000-0000-7000-8000-000000000025', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000010', 'dotnet', NULL, 'runtime', '10.0', 'unknown', 'none', 'repository', NULL, now(), now()),
                ('01900000-0000-7000-8000-000000000026', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000010', NULL, 'lodash', 'library', '4.17', 'upstream', 'none', 'repository', 'pkg:npm/lodash@4.17', now(), now());
            INSERT INTO intel.security_entry (id, kind, primary_key, state, changed_at, revision, material_fingerprint, remediation_fingerprint, exploitation, fix_available, cwe_ids, authority_warning, provenance) VALUES
                ('01900000-0000-7000-8000-000000000031', 'vulnerability', 'CVE-2026-0031', 'published', now(), 1, 'm', 'r', 'poc', false, '{}', false, '{}'),
                ('01900000-0000-7000-8000-000000000032', 'vulnerability', 'CVE-2026-0032', 'published', now(), 1, 'm', 'r', 'none', false, '{}', false, '{}'),
                ('01900000-0000-7000-8000-000000000033', 'vulnerability', 'CVE-2026-0033', 'published', now(), 1, 'm', 'r', 'none', false, '{}', false, '{}');
            INSERT INTO assess.alert (id, organization_id, entry_id, entry_key, number, level, assessed_entry_revision, created_at, changed_at) VALUES
                ('01900000-0000-7000-8000-000000000041', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000031', 'CVE-2026-0031', 1, 4, 1, now(), now()),
                ('01900000-0000-7000-8000-000000000042', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000032', 'CVE-2026-0032', 2, 3, 1, now(), now()),
                ('01900000-0000-7000-8000-000000000043', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000033', 'CVE-2026-0033', 3, 4, 1, now(), now());
            -- Without a version: one in review (two entries), one open (one entry shared with the first), one closed before.
            -- With a version: one in review, assigned and with a deadline.
            INSERT INTO assess.remediation (id, organization_id, project_id, component_id, number, level, open_findings, exploitation, fix_available, status, assigned_to, due_at, due_at_manual, opened_at, closed_at, close_reason, is_backfill, changed_at) VALUES
                ('01900000-0000-7000-8000-000000000051', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000021', 4, 4, 2, 'none', false, 'in_review', NULL, NULL, false, '2026-11-01', NULL, NULL, false, now()),
                ('01900000-0000-7000-8000-000000000052', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000022', 5, 4, 1, 'none', false, 'open', NULL, NULL, false, '2026-11-05', NULL, NULL, false, now()),
                ('01900000-0000-7000-8000-000000000053', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000021', 6, 3, 0, 'none', false, 'closed', NULL, NULL, false, '2026-10-01', '2026-10-02', 'risk_accepted', false, now()),
                ('01900000-0000-7000-8000-000000000054', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000023', 7, 4, 1, 'none', false, 'in_review', '01900000-0000-7000-8000-000000000099', now() + interval '7 days', true, '2026-11-01', NULL, NULL, false, now());
            INSERT INTO assess.finding (id, organization_id, alert_id, entry_id, project_id, component_id, remediation_id, match_type, version_verdict, verdict, verdict_source, level, evidence, factors, status, status_changed_at, is_backfill, created_at, changed_at, exploitation, fix_available) VALUES
                ('01900000-0000-7000-8000-000000000061', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000041', '01900000-0000-7000-8000-000000000031', '01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000021', '01900000-0000-7000-8000-000000000051', 'product', 'unknown', 'uncertain', 'rules', 4, '{}', '{"rulesetVersion": 1, "level": 4}', 'in_review', now(), false, now(), now(), 'none', false),
                ('01900000-0000-7000-8000-000000000062', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000042', '01900000-0000-7000-8000-000000000032', '01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000021', '01900000-0000-7000-8000-000000000051', 'product', 'unknown', 'uncertain', 'rules', 3, '{}', '{"rulesetVersion": 1, "level": 3}', 'in_review', now(), false, now(), now(), 'none', false),
                ('01900000-0000-7000-8000-000000000063', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000041', '01900000-0000-7000-8000-000000000031', '01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000022', '01900000-0000-7000-8000-000000000052', 'name', 'unknown', 'uncertain', 'rules', 4, '{}', '{"rulesetVersion": 1, "level": 4}', 'open', now(), false, now(), now(), 'none', false),
                ('01900000-0000-7000-8000-000000000064', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000043', '01900000-0000-7000-8000-000000000033', '01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000023', '01900000-0000-7000-8000-000000000054', 'product', 'in_range', 'affected', 'rules', 4, '{}', '{"rulesetVersion": 1, "level": 4}', 'in_review', now(), false, now(), now(), 'none', false);
            INSERT INTO notify.rule (organization_id, name, enabled, project_ids, min_level, events, created_at, changed_at) VALUES
                ('01900000-0000-7000-8000-000000000001', 'Team', true, '{}', 2, ARRAY['action.assigned', 'action.note_added'], now(), now()),
                ('01900000-0000-7000-8000-000000000001', 'Assignments only', true, '{}', 2, ARRAY['action.assigned'], now(), now());
            """, ct);

        var outcome = await migrator.MigrateAsync(owner, ct);
        Assert.Equal(pending, outcome.Applied);
        Assert.True(pending >= Added.Length);

        // Every organisation has its stack.
        Assert.Equal("Acme Stack, Empty Stack", await ScalarAsync<string>(owner,
            "SELECT string_agg(o.name || ' ' || p.name, ', ' ORDER BY o.name) FROM assets.project p JOIN iam.organization o ON o.id = p.organization_id WHERE p.kind = 'organization_stack'", ct));
        Assert.Equal("project", await ScalarAsync<string>(owner, "SELECT kind FROM assets.project WHERE id = '01900000-0000-7000-8000-000000000010'", ct));

        // Every component has the precision that follows from its version.
        Assert.Equal("21 none, 22 none, 23 exact, 24 line, 25 line, 26 exact", await ScalarAsync<string>(owner,
            "SELECT string_agg(right(id::text, 2) || ' ' || version_precision, ', ' ORDER BY id) FROM assets.component", ct));
        Assert.Equal(0L, await ScalarAsync<long>(owner, "SELECT count(*) FROM assets.component WHERE notice_threshold <> 'only_important'", ct));

        // Open actions without a version became notices, one per entry, naming every component that raised it.
        Assert.Equal(
            "CVE-2026-0031 open {01900000-0000-7000-8000-000000000021,01900000-0000-7000-8000-000000000022} 2026-11-01; CVE-2026-0032 open {01900000-0000-7000-8000-000000000021} 2026-11-01",
            await ScalarAsync<string>(owner,
                """
                SELECT string_agg(entry_key || ' ' || state || ' ' || (SELECT array_agg(c ORDER BY c) FROM unnest(component_ids) c)::text || ' ' || to_char(raised_at AT TIME ZONE 'UTC', 'YYYY-MM-DD'), '; ' ORDER BY entry_key)
                FROM assess.notice WHERE decided_at IS NULL AND remediation_id IS NULL
                """, ct));

        // They carry their entry's exploitation for the order of the list (task 5a.3, ADR 047).
        Assert.Equal("CVE-2026-0031 poc, CVE-2026-0032 none", await ScalarAsync<string>(owner,
            "SELECT string_agg(entry_key || ' ' || exploitation, ', ' ORDER BY entry_key) FROM assess.notice", ct));

        // Their actions closed saying so; their findings too, at level 1, and both histories say it.
        Assert.Equal("4 closed moved_to_notice 0, 5 closed moved_to_notice 0, 6 closed risk_accepted 0, 7 open - 1", await ScalarAsync<string>(owner,
            "SELECT string_agg(number || ' ' || status || ' ' || coalesce(close_reason, '-') || ' ' || open_findings, ', ' ORDER BY number) FROM assess.remediation", ct));
        Assert.Equal("61 not_affected moved_to_notice 1, 62 not_affected moved_to_notice 1, 63 not_affected moved_to_notice 1, 64 open - 4", await ScalarAsync<string>(owner,
            "SELECT string_agg(right(id::text, 2) || ' ' || status || ' ' || coalesce(status_reason, '-') || ' ' || level, ', ' ORDER BY id) FROM assess.finding", ct));
        Assert.Equal("moved_to_notice", await ScalarAsync<string>(owner, "SELECT DISTINCT factors->>'exclusion' FROM assess.finding WHERE status_reason = 'moved_to_notice'", ct));
        Assert.Equal("action.withdrawn 2, finding.closed 3", await ScalarAsync<string>(owner,
            "SELECT string_agg(kind || ' ' || n, ', ' ORDER BY kind) FROM (SELECT kind, count(*) AS n FROM assess.assess_event WHERE data->>'reason' = 'moved_to_notice' GROUP BY kind) e", ct));
        Assert.Equal("1 1, 2 1, 3 4", await ScalarAsync<string>(owner, "SELECT string_agg(number || ' ' || level, ', ' ORDER BY number) FROM assess.alert", ct));

        // The action with a version stays, open instead of in review; its retired columns keep what they held, unread.
        Assert.Equal(0L, await ScalarAsync<long>(owner, "SELECT count(*) FROM assess.remediation WHERE status = 'in_review'", ct));
        Assert.True(await ScalarAsync<bool>(owner, "SELECT due_at_manual AND assigned_to IS NOT NULL FROM assess.remediation WHERE number = 7", ct));

        // Rules no longer name the assignment.
        // A rule that named only the assignment would match nothing and could not be saved again: it is gone (ADR 046).
        Assert.Equal(0L, await ScalarAsync<long>(owner, "SELECT count(*) FROM notify.rule WHERE name = 'Assignments only'", ct));
        Assert.Equal("{action.note_added}", await ScalarAsync<string>(owner, "SELECT events::text FROM notify.rule WHERE name = 'Team'", ct));

        // During the upgrade the previous API and worker still run (master plan 4.11): what they write is accepted. A
        // component with a version but without a precision gets the one that follows from it, «in review» is stored as open.
        await ExecuteAsync(owner, """
            INSERT INTO assets.component (id, organization_id, project_id, product_key, custom_name, kind, version, packaging, protection, origin, purl, created_at, changed_at) VALUES
                ('01900000-0000-7000-8000-000000000027', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000010', 'nginx', NULL, 'network', '1.27', 'unknown', 'none', 'manual', NULL, now(), now()),
                ('01900000-0000-7000-8000-000000000028', '01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000010', NULL, 'express', 'library', E'4.21.2\t', 'upstream', 'none', 'repository', 'pkg:npm/express', now(), now());
            UPDATE assets.component SET version = NULL WHERE id = '01900000-0000-7000-8000-000000000023';
            UPDATE assess.remediation SET status = 'in_review' WHERE number = 7;
            UPDATE assess.finding SET status = 'in_review' WHERE id = '01900000-0000-7000-8000-000000000064';
            """, ct);
        Assert.Equal("23 none, 27 exact, 28 exact", await ScalarAsync<string>(owner,
            "SELECT string_agg(right(id::text, 2) || ' ' || version_precision, ', ' ORDER BY id) FROM assets.component WHERE right(id::text, 2) IN ('23', '27', '28')", ct));
        Assert.Equal("open open", await ScalarAsync<string>(owner,
            "SELECT r.status || ' ' || f.status FROM assess.remediation r JOIN assess.finding f ON f.remediation_id = r.id WHERE r.number = 7", ct));

        // An organisation the previous API creates during the upgrade gets its stack from the next migrate (ADR 046).
        await ExecuteAsync(owner, "INSERT INTO iam.organization (id, name, slug, locale, time_zone, settings, created_at) VALUES ('01900000-0000-7000-8000-000000000003', 'Late', 'late', 'en', 'UTC', '{}', now())", ct);
        Assert.Equal(0, (await migrator.MigrateAsync(owner, ct)).Applied);
        Assert.Equal(1L, await ScalarAsync<long>(owner, "SELECT count(*) FROM assets.project WHERE organization_id = '01900000-0000-7000-8000-000000000003' AND kind = 'organization_stack'", ct));
        Assert.Equal(3L, await ScalarAsync<long>(owner, "SELECT count(*) FROM assets.project WHERE kind = 'organization_stack'", ct));
    }

    private DatabaseOptions Options(string database) => new()
    {
        Host = server.Host,
        Port = server.Port,
        Database = database,
        OwnerUser = PostgresServer.OwnerUser,
        OwnerPassword = PostgresServer.OwnerPassword,
        AppPassword = "app-test-password",
    };

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }

    private sealed class NoDump : IBackupWriter
    {
        public Task<string?> WriteAsync(NpgsqlConnection owner, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
