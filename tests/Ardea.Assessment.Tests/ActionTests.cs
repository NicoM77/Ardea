using Ardea.Assessment.Features;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assessment.Tests;

/// <summary>
/// Findings, actions, alerts and their history (task 3.5; master plan 7.4, 7.7, 4.9, 8.2): actions per
/// component, events as history rows, numbers from the organisation's counter, a first fill with one
/// summary, closing by a raised version, and the status changes people make.
/// </summary>
public sealed class ActionTests(PostgresServer server)
{
    private static readonly Guid Alice = Guid.CreateVersion7();

    [Fact]
    public async Task A_new_project_with_40_hits_gets_backfill_findings_and_exactly_one_summary()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        for (var i = 0; i < 40; i++)
        {
            await host.AddEntryAsync(new TestEntry($"CVE-2026-{7000 + i}", new TestAffected { ProductKey = i % 2 == 0 ? "postgresql" : "nginx", Fixed = "99.0" })
            {
                Score = i == 7 ? 9.8 : 5.0,
            });
        }

        var project = await host.AddProjectAsync(acme, "Shop");
        await host.AddComponentAsync(acme, project, c => (c.ProductKey, c.Kind, c.Version) = ("postgresql", "database", "16.2"));
        await host.AddComponentAsync(acme, project, c => (c.ProductKey, c.Kind, c.Version) = ("nginx", "network", "1.27.0"));
        await host.ProfileChangedAsync(acme, project);
        await host.RunJobsAsync();

        Assert.Equal(40L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.finding WHERE is_backfill AND status = 'open'"));
        Assert.Equal(["2\topen\tTrue"], await host.RowsAsync("SELECT count(*), min(status), bool_and(is_backfill) FROM assess.remediation"));
        Assert.Equal(["backfill.completed"], await host.RowsAsync("SELECT kind FROM assess.assess_event"));
        Assert.Equal(
            """{"scope": "project", "actions": 2, "findings": 40, "highestLevel": 4, "repositoryLinks": []}""",
            await host.ScalarAsync<string>("SELECT data::text FROM assess.assess_event WHERE kind = 'backfill.completed'"));

        // Alerts and actions share one number range; every number is handed out once.
        Assert.Equal(42L, await host.ScalarAsync<long>(
            "SELECT count(DISTINCT n) FROM (SELECT number AS n FROM assess.alert UNION ALL SELECT number FROM assess.remediation) x WHERE n IS NOT NULL"));
        Assert.Equal(43, await host.ScalarAsync<int>("SELECT next_number FROM assess.org_counter"));

        // Later the forward search is no first fill: a new hit on a third component raises an action.
        var redis = await host.AddComponentAsync(acme, project, c => (c.ProductKey, c.Kind, c.Version) = ("redis", "database", "7.2.0"));
        await host.ProfileChangedAsync(acme, project);
        await host.RunJobsAsync();
        var entry = await host.AddEntryAsync(new TestEntry("CVE-2026-7100", new TestAffected { ProductKey = "redis", Fixed = "7.2.6" }));
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        Assert.Equal(["backfill.completed", "action.raised"], await host.RowsAsync("SELECT kind FROM assess.assess_event WHERE kind NOT LIKE 'finding.%' ORDER BY at, id"));
        Assert.Equal(1L, await host.ScalarAsync<long>($"SELECT count(*) FROM assess.remediation WHERE component_id = '{redis}' AND NOT is_backfill"));
    }

    [Fact]
    public async Task An_action_bundles_the_findings_of_its_component_and_reports_what_changes()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var (project, component) = await ProjectAsync(host, acme);

        var first = await host.AddEntryAsync(new TestEntry("CVE-2026-8001", new TestAffected { ProductKey = "postgresql", Introduced = "16.0", Fixed = "16.3" }) { Score = 5.0 });
        await host.EntryChangedAsync(first);
        await host.RunJobsAsync();
        Assert.Equal(["2\t3\t16.3\t1\topen"], await host.RowsAsync("SELECT number, level, target_version, open_findings, status FROM assess.remediation"));
        Assert.Equal(1, await host.ScalarAsync<int>("SELECT number FROM assess.alert"));

        // A second finding without a higher level joins quietly.
        var second = await host.AddEntryAsync(new TestEntry("CVE-2026-8002", new TestAffected { ProductKey = "postgresql", Introduced = "16.0", Fixed = "16.4" }) { Score = 5.0 });
        await host.EntryChangedAsync(second);
        await host.RunJobsAsync();
        Assert.Equal(["3\t16.4\t2"], await host.RowsAsync("SELECT level, target_version, open_findings FROM assess.remediation"));

        // A third raises the level, and the deadline starts again from the raise.
        var third = await host.AddEntryAsync(new TestEntry("CVE-2026-8003", new TestAffected { ProductKey = "postgresql", Fixed = "16.4" }) { Score = 9.8 });
        await host.EntryChangedAsync(third);
        await host.RunJobsAsync();
        Assert.Equal(["4\t3\tTrue"], await host.RowsAsync("SELECT level, open_findings, due_at = level_raised_at + interval '7 days' FROM assess.remediation"));

        // Exploitation becomes known without a higher level: its own event.
        await host.ScalarAsync<int>($"UPDATE intel.security_entry SET exploitation = 'poc', revision = 2 WHERE id = '{first}' RETURNING 1");
        await host.EntryChangedAsync(first);
        await host.RunJobsAsync();

        // The version is raised in the profile: every finding closes as fixed by update, and so does the action.
        await host.ChangeComponentAsync(acme, component, c => c.Version = "16.4");
        await host.ProfileChangedAsync(acme, project);
        await host.RunJobsAsync();
        Assert.Equal(["closed\tfixed_by_update\t0"], await host.RowsAsync("SELECT status, close_reason, open_findings FROM assess.remediation"));
        Assert.Equal(
            ["action.raised", "action.level_raised", "action.exploitation_escalated", "action.withdrawn"],
            await host.RowsAsync("SELECT kind FROM assess.assess_event WHERE kind LIKE 'action.%' ORDER BY at, id"));
        Assert.Equal(3L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assess_event WHERE kind = 'finding.closed'"));

        // Every history row has its own id, the event's id for the notifications (master plan 8.2).
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) - count(DISTINCT id) FROM assess.assess_event"));
    }

    [Fact]
    public async Task People_set_the_status_with_the_expected_starting_status_and_can_take_it_back()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var (_, _) = await ProjectAsync(host, acme);
        var entry = await host.AddEntryAsync(new TestEntry("CVE-2026-9001", new TestAffected { ProductKey = "postgresql", Fixed = "16.4" }));
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        var number = await host.ScalarAsync<int>("SELECT number FROM assess.remediation");

        // A stale starting status is refused.
        var stale = await CommandAsync(host, acme, c => c.SetActionStatusAsync(number, new SetActionStatusRequest("in_review", "in_review", null), Alice, default));
        Assert.Equal("status_conflict", stale.Refusal?.Code);

        await CommandAsync(host, acme, c => c.SetActionStatusAsync(number, new SetActionStatusRequest("in_review", "open", "Checking the cluster"), Alice, default));
        Assert.Equal(["in_review\tin_review"], await host.RowsAsync("SELECT r.status, f.status FROM assess.remediation r JOIN assess.finding f ON f.remediation_id = r.id"));

        await CommandAsync(host, acme, c => c.SetActionStatusAsync(number, new SetActionStatusRequest("risk_accepted", "in_review", "Internal only"), Alice, default));
        Assert.Equal(["closed\trisk_accepted\trisk_accepted"], await host.RowsAsync("SELECT r.status, r.close_reason, f.status FROM assess.remediation r JOIN assess.finding f ON f.remediation_id = r.id"));

        // A reassessment with the same level leaves a person's decision alone.
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        Assert.Equal("risk_accepted", await host.ScalarAsync<string>("SELECT status FROM assess.finding"));

        // Taking it back opens the action again with its findings.
        await CommandAsync(host, acme, c => c.SetActionStatusAsync(number, new SetActionStatusRequest("open", "closed", null), Alice, default));
        Assert.Equal(["open\topen"], await host.RowsAsync("SELECT r.status, f.status FROM assess.remediation r JOIN assess.finding f ON f.remediation_id = r.id"));
        Assert.Equal(
            ["action.raised\t-", "action.status_changed\tin_review", "action.status_changed\trisk_accepted", "action.status_changed\topen"],
            await host.RowsAsync("SELECT kind, coalesce(data->>'to', '-') FROM assess.assess_event WHERE kind LIKE 'action.%' ORDER BY at, id"));
    }

    [Fact]
    public async Task A_finding_a_person_closed_opens_again_when_its_level_rises()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        await ProjectAsync(host, acme);
        var entry = await host.AddEntryAsync(new TestEntry("CVE-2026-9101", new TestAffected { ProductKey = "postgresql", Fixed = "16.4" }) { Score = 5.0 });
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        var finding = await host.ScalarAsync<Guid>("SELECT id FROM assess.finding");

        await CommandAsync(host, acme, c => c.SetFindingStatusAsync(finding, new SetFindingStatusRequest("not_affected", "open", "Feature not used"), Alice, default));
        Assert.Equal(["not_affected\t1\tclosed"], await host.RowsAsync("SELECT f.status, f.level, r.status FROM assess.finding f JOIN assess.remediation r ON r.id = f.remediation_id"));

        // Actively exploited now: the level rises above what the person saw, so it opens again in a new action (master plan 6.7).
        await host.ScalarAsync<int>($"UPDATE intel.security_entry SET exploitation = 'active', revision = 2 WHERE id = '{entry}' RETURNING 1");
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        Assert.Equal(["open\t4"], await host.RowsAsync("SELECT status, level FROM assess.finding"));
        Assert.Equal(["closed", "open"], await host.RowsAsync("SELECT status FROM assess.remediation ORDER BY number"));
    }

    [Fact]
    public async Task Assignment_deadline_and_notes_go_into_the_history()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        await ProjectAsync(host, acme);
        var entry = await host.AddEntryAsync(new TestEntry("CVE-2026-9201", new TestAffected { ProductKey = "postgresql", Fixed = "16.4" }) { Score = 5.0 });
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        var number = await host.ScalarAsync<int>("SELECT number FROM assess.remediation");

        var due = new DateTimeOffset(2026, 12, 24, 12, 0, 0, TimeSpan.Zero);
        await CommandAsync(host, acme, c => c.AddNoteAsync(number, new AddNoteRequest("Patch window on Friday"), Alice, default));
        await host.InOrganizationAsync(acme, async services =>
        {
            var commands = services.GetRequiredService<ActionCommands>();
            var result = await commands.UpdateActionAsync(number, new UpdateActionRequest(null, null, due, null), Alice, new NoMembers(), default);
            Assert.Null(result.Refusal);
        });
        Assert.Equal(["True\t2026-12-24"], await host.RowsAsync("SELECT due_at_manual, to_char(due_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') FROM assess.remediation"));

        // A new finding with a higher level does not move a deadline a person set.
        var worse = await host.AddEntryAsync(new TestEntry("CVE-2026-9202", new TestAffected { ProductKey = "postgresql", Fixed = "16.4" }) { Score = 9.8 });
        await host.EntryChangedAsync(worse);
        await host.RunJobsAsync();
        Assert.Equal("2026-12-24", await host.ScalarAsync<string>("SELECT to_char(due_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') FROM assess.remediation"));

        Assert.Equal(
            ["action.raised", "action.note_added", "action.level_raised"],
            await host.RowsAsync("SELECT kind FROM assess.assess_event WHERE kind LIKE 'action.%' ORDER BY at, id"));
    }

    private static async Task<(Guid Project, Guid Component)> ProjectAsync(AssessmentTestHost host, Guid organization)
    {
        var project = await host.AddProjectAsync(organization, "Platform Ops", criticality: "high");
        var component = await host.AddComponentAsync(organization, project, c => (c.ProductKey, c.Kind, c.Version) = ("postgresql", "database", "16.2"));
        await host.ProfileChangedAsync(organization, project);
        await host.RunJobsAsync();
        return (project, component);
    }

    private static async Task<CommandResult<T>> CommandAsync<T>(AssessmentTestHost host, Guid organization, Func<ActionCommands, Task<CommandResult<T>>> command)
    {
        CommandResult<T> result = default;
        await host.InOrganizationAsync(organization, async services => result = await command(services.GetRequiredService<ActionCommands>()));
        return result;
    }

    private sealed class NoMembers : Platform.Tenancy.IOrganizationMembership
    {
        public Task<Platform.Tenancy.OrganizationAccess?> FindAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<Platform.Tenancy.OrganizationAccess?>(null);
    }
}
