using Ardea.TestSupport;
using Npgsql;

namespace Ardea.Assessment.Tests;

public sealed class QueryPlanTests(PostgresServer server)
{
    [Theory]
    [InlineData("SELECT a.entry_id FROM intel.entry_affected a WHERE a.product_key = ANY (@texts)", "ix_entry_affected_product_key")]
    [InlineData("SELECT a.entry_id FROM intel.entry_affected a JOIN unnest(@texts::text[], @texts::text[]) AS k(vendor, product) ON a.cpe_vendor = k.vendor AND a.cpe_product = k.product", "ix_entry_affected_cpe")]
    [InlineData("SELECT a.entry_id FROM intel.entry_affected a JOIN unnest(@texts::text[], @texts::text[]) AS k(type, name) ON a.purl_type = k.type AND a.purl_name = k.name", "ix_entry_affected_purl")]
    [InlineData("SELECT DISTINCT k.organization_id FROM assets.component_key k JOIN unnest(@texts::text[], @texts::text[]) AS q(key_type, key_value) ON k.key_type = q.key_type AND k.key_value = q.key_value", "ix_component_key_value")]
    [InlineData("SELECT k.component_id FROM assets.component_key k WHERE k.key_type = 'cpe' AND k.key_value = ANY (@texts)", "ix_component_key_value")]
    [InlineData("SELECT a.organization_id FROM assess.alert a WHERE a.entry_id = @id", "ix_alert_entry")]
    [InlineData("SELECT DISTINCT f.entry_id FROM assess.finding f WHERE f.project_id = @id", "ix_finding_project_entry")]
    [InlineData("SELECT * FROM assess.finding f WHERE f.alert_id = @id", "ux_finding_component")]
    [InlineData("SELECT * FROM assess.finding f WHERE f.component_id = @id", "ix_finding_component")]
    [InlineData("SELECT * FROM assess.remediation r WHERE r.component_id = @id AND r.status <> 'closed'", "ux_remediation_component_open")]
    [InlineData("SELECT * FROM assess.finding f WHERE f.remediation_id = @id", "ix_finding_remediation")]
    [InlineData("SELECT * FROM assess.assess_event e WHERE e.remediation_id = @id ORDER BY e.at", "ix_assess_event_remediation")]
    public async Task Candidate_search_queries_use_their_indexes(string sql, string index)
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        await using var connection = new NpgsqlConnection(host.Database.OwnerConnectionString("tests"));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var off = new NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await off.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var explain = new NpgsqlCommand("EXPLAIN " + sql, connection);
        explain.Parameters.Add(new NpgsqlParameter("texts", new[] { "postgresql", "lodash" }));
        explain.Parameters.Add(new NpgsqlParameter("id", Guid.CreateVersion7()));
        var plan = new List<string>();
        await using var reader = await explain.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            plan.Add(reader.GetString(0));
        }

        Assert.True(plan.Exists(line => line.Contains(index, StringComparison.Ordinal)), string.Join('\n', plan));
    }
}
