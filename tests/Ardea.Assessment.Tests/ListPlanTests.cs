using Ardea.TestSupport;
using Npgsql;

namespace Ardea.Assessment.Tests;

public sealed class ListPlanTests(PostgresServer server)
{
    [Theory]
    [InlineData("SELECT * FROM assess.remediation r WHERE r.organization_id = @org AND r.status <> 'closed' ORDER BY r.level DESC, r.number DESC LIMIT 51", "ix_remediation_level")]
    [InlineData("SELECT * FROM assess.remediation r WHERE r.organization_id = @org ORDER BY r.level DESC, r.number DESC LIMIT 51", "ix_remediation_level")]
    [InlineData("SELECT * FROM assess.remediation r WHERE r.organization_id = @org AND r.status = 'open' ORDER BY r.level DESC, r.number DESC LIMIT 51", "ix_remediation_list|ix_remediation_level")]
    [InlineData("SELECT * FROM assess.remediation r WHERE r.organization_id = @org AND r.status <> 'closed' AND r.level = ANY (@levels) ORDER BY r.level DESC, r.number DESC LIMIT 51", "ix_remediation_level")]
    [InlineData("SELECT * FROM assess.finding f WHERE f.organization_id = @org ORDER BY f.level DESC, f.id DESC LIMIT 51", "ix_finding_org_level")]
    [InlineData("SELECT * FROM assess.finding f WHERE f.organization_id = @org AND (f.level < @level OR (f.level = @level AND f.id < @id)) ORDER BY f.level DESC, f.id DESC LIMIT 51", "ix_finding_org_level")]
    [InlineData("SELECT * FROM assess.alert a WHERE a.organization_id = @org AND a.number IS NOT NULL ORDER BY a.level DESC, a.number DESC LIMIT 51", "ix_alert_list")]
    [InlineData("SELECT * FROM assess.alert a WHERE a.organization_id = @org AND a.number IS NOT NULL AND a.level = ANY (@levels) ORDER BY a.level DESC, a.number DESC LIMIT 51", "ix_alert_list")]
    [InlineData("SELECT * FROM assess.notice n WHERE n.organization_id = @org AND n.state = 'open' AND NOT n.is_backfill ORDER BY n.exploited DESC, n.score_rank DESC, n.raised_at DESC, n.id DESC LIMIT 51", "ix_notice_list")]
    [InlineData("SELECT * FROM assess.notice n WHERE n.organization_id = @org AND n.state = 'open' AND n.is_backfill ORDER BY n.exploited DESC, n.score_rank DESC, n.raised_at DESC, n.id DESC LIMIT 51", "ix_notice_list")]
    [InlineData("SELECT * FROM assess.notice n WHERE n.organization_id = @org AND n.state = 'open' AND NOT n.is_backfill AND (NOT n.exploited OR (n.score_rank < @score)) ORDER BY n.exploited DESC, n.score_rank DESC, n.raised_at DESC, n.id DESC LIMIT 51", "ix_notice_list")]
    public async Task Lists_read_a_page_in_index_order(string sql, string index)
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var plan = await QueryPlans.ExplainAsync(
            host.Database.OwnerConnectionString("tests"), sql, TestContext.Current.CancellationToken,
            new NpgsqlParameter("org", Guid.CreateVersion7()), new NpgsqlParameter("levels", new[] { 4, 5 }),
            new NpgsqlParameter("number", 7), new NpgsqlParameter("level", 3), new NpgsqlParameter("id", Guid.CreateVersion7()), new NpgsqlParameter("score", 9.1));

        Assert.True(plan.Any(line => index.Split('|').Any(name => line.Contains(name, StringComparison.Ordinal))), string.Join('\n', plan));
        Assert.False(QueryPlans.SortsEverything(plan), string.Join('\n', plan));
    }
}
