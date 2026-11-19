using Ardea.TestSupport;
using Npgsql;

namespace Ardea.Assessment.Tests;

/// <summary>
/// The lists of the actions page (actions, findings, alerts) read a page of rows in index order, within the
/// organisation, most severe first (master plan 4.4, 10.2). Each query is written as EF Core sends it for the
/// endpoint's filters; the tenant filter is part of every query. The plan must name the index and must not
/// sort the whole list.
/// </summary>
public sealed class ListPlanTests(PostgresServer server)
{
    [Theory]
    // Actions: «active» (the default), «all», one status; with a level filter. One status may use either index.
    [InlineData("SELECT * FROM assess.remediation r WHERE r.organization_id = @org AND r.status <> 'closed' ORDER BY r.level DESC, r.number DESC LIMIT 51", "ix_remediation_level")]
    [InlineData("SELECT * FROM assess.remediation r WHERE r.organization_id = @org ORDER BY r.level DESC, r.number DESC LIMIT 51", "ix_remediation_level")]
    [InlineData("SELECT * FROM assess.remediation r WHERE r.organization_id = @org AND r.status = 'open' ORDER BY r.level DESC, r.number DESC LIMIT 51", "ix_remediation_list|ix_remediation_level")]
    [InlineData("SELECT * FROM assess.remediation r WHERE r.organization_id = @org AND r.status <> 'closed' AND r.level = ANY (@levels) ORDER BY r.level DESC, r.number DESC LIMIT 51", "ix_remediation_level")]
    // Findings: by level, then newest id; the cursor continues after the last row.
    [InlineData("SELECT * FROM assess.finding f WHERE f.organization_id = @org ORDER BY f.level DESC, f.id DESC LIMIT 51", "ix_finding_org_level")]
    [InlineData("SELECT * FROM assess.finding f WHERE f.organization_id = @org AND (f.level < @level OR (f.level = @level AND f.id < @id)) ORDER BY f.level DESC, f.id DESC LIMIT 51", "ix_finding_org_level")]
    // Alerts: by level, then number; search by number or entry key.
    [InlineData("SELECT * FROM assess.alert a WHERE a.organization_id = @org AND a.number IS NOT NULL ORDER BY a.level DESC, a.number DESC LIMIT 51", "ix_alert_list")]
    [InlineData("SELECT * FROM assess.alert a WHERE a.organization_id = @org AND a.number IS NOT NULL AND a.level = ANY (@levels) ORDER BY a.level DESC, a.number DESC LIMIT 51", "ix_alert_list")]
    public async Task Lists_read_a_page_in_index_order(string sql, string index)
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var plan = await QueryPlans.ExplainAsync(
            host.Database.OwnerConnectionString("tests"), sql, TestContext.Current.CancellationToken,
            new NpgsqlParameter("org", Guid.CreateVersion7()), new NpgsqlParameter("levels", new[] { 4, 5 }),
            new NpgsqlParameter("number", 7), new NpgsqlParameter("level", 3), new NpgsqlParameter("id", Guid.CreateVersion7()));

        Assert.True(plan.Any(line => index.Split('|').Any(name => line.Contains(name, StringComparison.Ordinal))), string.Join('\n', plan));
        Assert.False(QueryPlans.SortsEverything(plan), string.Join('\n', plan));
    }
}
