using Ardea.Assessment.Features;
using Ardea.Assessment.Persistence;
using Ardea.Platform.Ai;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assessment.Tests.Estimating;

/// <summary>
/// «Einschätzung korrigieren» (task 4.7; master plan 7.6, 7.7, 7.9): a person confirms or corrects the model's estimate
/// of a finding. The review is feedback per model and prompt version, a history row, and a reassessment of that finding
/// alone, in which the floor «at most one level below the result without the model» no longer binds.
/// </summary>
public sealed class ReviewTests(PostgresServer server)
{
    private static readonly Guid Alice = Guid.CreateVersion7();

    [Fact]
    public async Task Confirming_the_models_unlikely_makes_it_a_persons_verdict_without_the_floor()
    {
        await using var host = await StartAsync("unlikely");
        var acme = AssessmentTestHost.NewOrganization();
        var (finding, other) = await TwoUnknownVersionsAsync(host, acme);

        // CVSS 8.0 (3) and a public exploit (2) with an unknown version: 5 points, level 4. «Unlikely» (−2) gives 3
        // points and level 2, held at 3 by the floor of the model.
        Assert.Equal("unlikely\tmodel\t3\t4", await RowAsync(host, finding));

        // What the person saw no longer holds: refused, nothing written.
        var stale = await ReviewAsync(host, acme, finding, new ReviewVerdictRequest("confirm", "likely", "model", null, null, null));
        Assert.Equal("verdict_conflict", stale.Refusal?.Code);
        var invalid = await ReviewAsync(host, acme, finding, new ReviewVerdictRequest("correct", "unlikely", "model", "affected", null, " "));
        Assert.Equal("invalid_review", invalid.Refusal?.Code);
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assessment_feedback"));

        var confirmed = await ReviewAsync(host, acme, finding, new ReviewVerdictRequest("confirm", "unlikely", "model", null, null, null));
        Assert.Null(confirmed.Refusal);

        // A person's verdict: the floor no longer binds, the level is the rules' with «unlikely». Only this finding moved.
        Assert.Equal("unlikely\thuman\t2\t-", await RowAsync(host, finding));
        Assert.Equal("unlikely\tmodel\t3\t4", await RowAsync(host, other));
        Assert.Equal(
            "confirmed\tmodel\tunlikely\tunlikely\t3\t-\tTrue",
            Assert.Single(await host.RowsAsync("""
                SELECT f.kind, f.verdict_source, f.verdict_before, f.verdict, f.level_before, coalesce(f.expected_level::text, '-'), f.assessment_id = a.current_assessment_id
                FROM assess.assessment_feedback f JOIN assess.alert a ON a.id = f.alert_id
                """)));
        Assert.Equal(
            ["finding.verdict_confirmed\t2"],
            await host.RowsAsync($"""
                SELECT e.kind, r.level FROM assess.assess_event e JOIN assess.remediation r ON r.id = e.remediation_id
                WHERE e.finding_id = '{finding}' AND e.kind LIKE 'finding.verdict_%'
                """));
        Assert.Equal(
            ["4\t3", "3\t2"],
            await host.RowsAsync($"""
                SELECT e.data->>'from', e.data->>'to' FROM assess.assess_event e JOIN assess.finding f ON f.remediation_id = e.remediation_id
                WHERE f.id = '{finding}' AND e.kind = 'action.level_lowered' ORDER BY e.at
                """));

        // The model is not asked again for this, and a later reassessment keeps the person's verdict.
        var calls = await host.ScalarAsync<long>("SELECT count(*) FROM assess.assessment");
        await host.EntryChangedAsync(await host.ScalarAsync<Guid>($"SELECT entry_id FROM assess.finding WHERE id = '{finding}'"));
        await host.RunJobsAsync();
        Assert.Equal(calls, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assessment"));
        Assert.Equal("unlikely\thuman\t2\t-", await RowAsync(host, finding));

        // The correction rate per model and prompt version: one review, none corrected.
        Assert.Equal(
            ["local\tlocal-model\t1\t1\t2\t1\t0"],
            await host.RowsAsync("SELECT provider, model, prompt_version, assessments, verdicts, confirmed, corrected FROM assess.model_feedback_counts()"));
    }

    [Fact]
    public async Task Confirming_a_not_affected_proposal_closes_the_finding_and_a_reassessment_keeps_it_closed()
    {
        await using var host = await StartAsync("not_affected");
        var acme = AssessmentTestHost.NewOrganization();
        var (finding, _) = await TwoUnknownVersionsAsync(host, acme);
        Assert.Equal("not_affected\tmodel\t3\t4", await RowAsync(host, finding));

        Assert.Null((await ReviewAsync(host, acme, finding, new ReviewVerdictRequest("confirm", "not_affected", "model", null, null, "Not installed anywhere"))).Refusal);

        // The proposal is a person's exclusion now: level 1, closed by Alice, out of the action.
        Assert.Equal(
            "not_affected\thuman\t1\tnot_affected\tTrue\tNot installed anywhere",
            Assert.Single(await host.RowsAsync($"""
                SELECT verdict, verdict_source, level, status, status_changed_by = '{Alice}', status_note FROM assess.finding WHERE id = '{finding}'
                """)));
        Assert.Equal("closed", await host.ScalarAsync<string>($"SELECT r.status FROM assess.remediation r JOIN assess.finding f ON f.component_id = r.component_id WHERE f.id = '{finding}'"));

        // The same input again: the person's closing stands, the confirmed verdict does not count as a new reason to reopen.
        await host.EntryChangedAsync(await host.ScalarAsync<Guid>($"SELECT entry_id FROM assess.finding WHERE id = '{finding}'"));
        await host.RunJobsAsync();
        Assert.Equal("not_affected\thuman\t1\tnot_affected", Assert.Single(await host.RowsAsync($"SELECT verdict, verdict_source, level, status FROM assess.finding WHERE id = '{finding}'")));

        // Taking the closing back returns the verdict to the rules and the model.
        await host.InOrganizationAsync(acme, async services =>
        {
            var result = await services.GetRequiredService<ActionCommands>()
                .SetFindingStatusAsync(finding, new SetFindingStatusRequest("open", "not_affected", null), Alice, CancellationToken.None);
            Assert.Null(result.Refusal);
        });
        await host.RunJobsAsync();
        Assert.Equal("not_affected\tmodel\t3\topen", Assert.Single(await host.RowsAsync($"SELECT verdict, verdict_source, level, status FROM assess.finding WHERE id = '{finding}'")));
    }

    [Fact]
    public async Task A_correction_with_a_reason_sets_the_persons_verdict_or_records_the_expected_level()
    {
        await using var host = await StartAsync("unlikely");
        var acme = AssessmentTestHost.NewOrganization();
        var (finding, other) = await TwoUnknownVersionsAsync(host, acme);

        // A person knows better: «affected» confirms the affectedness, which lifts the cap of 4 as well (master plan 7.6 step 3).
        Assert.Null((await ReviewAsync(host, acme, finding, new ReviewVerdictRequest("correct", "unlikely", "model", "affected", null, "Same build as production"))).Refusal);
        Assert.Equal("affected\thuman\t4\t-", await RowAsync(host, finding));

        // Only the level is questioned: recorded for the quality figures, the rules keep setting the level.
        Assert.Null((await ReviewAsync(host, acme, other, new ReviewVerdictRequest("correct", "unlikely", "model", null, 2, "Only reachable from the admin network"))).Refusal);
        Assert.Equal("unlikely\tmodel\t3\t4", await RowAsync(host, other));

        Assert.Equal(
            ["corrected\taffected\t-\tSame build as production", "corrected\t-\t2\tOnly reachable from the admin network"],
            await host.RowsAsync("SELECT kind, coalesce(verdict, '-'), coalesce(expected_level::text, '-'), reason FROM assess.assessment_feedback ORDER BY created_at"));
        Assert.Equal(
            ["finding.verdict_corrected", "finding.verdict_corrected"],
            await host.RowsAsync("SELECT kind FROM assess.assess_event WHERE kind LIKE 'finding.verdict_%' ORDER BY at"));

        // The administration's figure, read without an organisation through the system function.
        IReadOnlyList<ModelQualityView> quality = [];
        await host.InSystemAsync(async services => quality = await ModelQualityEndpoints.ReadAsync(services.GetRequiredService<AssessDbContext>(), CancellationToken.None));
        Assert.Equal(new ModelQualityView("local", "local-model", "1", 1, 2, 0, 2, 1.0), Assert.Single(quality));

        // A person's verdict can be corrected again, but not confirmed: only the model's estimate is.
        var again = await ReviewAsync(host, acme, finding, new ReviewVerdictRequest("confirm", "affected", "human", null, null, null));
        Assert.Equal("nothing_to_confirm", again.Refusal?.Code);
    }

    private Task<AssessmentTestHost> StartAsync(string verdict) =>
        AssessmentTestHost.StartAsync(server, services => services.AddSingleton<IAiProviders>(
            new RecordedProviders(("local", true, new RecordedModel(request => Recorded.For("valid.json", request, verdict))))));

    /// <summary>
    /// Two projects with PostgreSQL 17.4 from a distribution, whose version the check cannot judge (a component without a
    /// version would raise a notice instead, ADR 047), one entry of CVSS 8.0 with a public exploit: two findings of one alert,
    /// both assessed by the model.
    /// </summary>
    private static async Task<(Guid Finding, Guid Other)> TwoUnknownVersionsAsync(AssessmentTestHost host, Guid organization)
    {
        var components = new List<Guid>();
        foreach (var name in new[] { "Shop", "Billing" })
        {
            var project = await host.AddProjectAsync(organization, name);
            components.Add(await host.AddComponentAsync(organization, project, c => (c.ProductKey, c.Kind, c.Version, c.Packaging) = ("postgresql", "database", "17.4", "distribution")));
            await host.ProfileChangedAsync(organization, project);
        }

        await host.RunJobsAsync();
        var entry = await host.AddEntryAsync(new TestEntry("CVE-2026-7701", new TestAffected { ProductKey = "postgresql", Introduced = "17", Fixed = "17.12" })
        {
            Score = 8.0,
            Exploitation = "poc",
        });
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        return (
            await host.ScalarAsync<Guid>($"SELECT id FROM assess.finding WHERE component_id = '{components[0]}'"),
            await host.ScalarAsync<Guid>($"SELECT id FROM assess.finding WHERE component_id = '{components[1]}'"));
    }

    private static async Task<string> RowAsync(AssessmentTestHost host, Guid finding) =>
        Assert.Single(await host.RowsAsync($"""
            SELECT verdict, verdict_source, level, coalesce(factors->>'levelWithoutModel', '-') FROM assess.finding WHERE id = '{finding}'
            """));

    private static async Task<CommandResult<Finding>> ReviewAsync(AssessmentTestHost host, Guid organization, Guid finding, ReviewVerdictRequest request)
    {
        CommandResult<Finding> result = default;
        await host.InOrganizationAsync(organization, async services =>
            result = await services.GetRequiredService<VerdictReviews>().ReviewAsync(finding, request, Alice, CancellationToken.None));
        return result;
    }
}
