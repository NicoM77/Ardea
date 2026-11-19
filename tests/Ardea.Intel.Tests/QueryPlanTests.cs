using Ardea.TestSupport;
using Npgsql;

namespace Ardea.Intel.Tests;

/// <summary>
/// The hot queries of the first load use their indexes (phase 1 acceptance: first load under an hour
/// on 2 vCPU). Each query is written exactly as EF Core or the job store sends it; with sequential
/// scans disabled the plan must name the index, which proves the expression matches.
/// </summary>
public sealed class QueryPlanTests(PostgresServer server)
{
    [Theory]
    [InlineData("SELECT entry_id FROM intel.entry_alias AS e WHERE e.scheme || ':' || e.value = ANY (@keys)", "ix_entry_alias_key")]
    [InlineData("SELECT * FROM intel.entry_signal AS e WHERE e.scheme || ':' || e.value = ANY (@keys)", "ix_entry_signal_key")]
    [InlineData("SELECT * FROM intel.observation AS o WHERE o.aliases && @keys", "ix_observation_aliases")]
    [InlineData("SELECT id FROM platform.job WHERE state = 'pending' AND run_at <= now() AND kind = 'intel.sync-source' ORDER BY priority, run_at LIMIT 4", "ix_job_due")]
    public async Task Correlation_and_job_claims_use_their_indexes(string sql, string index)
    {
        await using var host = await IntelTestHost.StartAsync(server);
        await using var connection = new NpgsqlConnection(host.Database.OwnerConnectionString("tests"));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var off = new NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await off.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var explain = new NpgsqlCommand("EXPLAIN " + sql, connection);
        explain.Parameters.Add(new NpgsqlParameter("keys", new[] { "CVE:CVE-2026-1", "GHSA:GHSA-aaaa-bbbb-cccc" }));
        var plan = new List<string>();
        await using var reader = await explain.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            plan.Add(reader.GetString(0));
        }

        Assert.Contains(plan, line => line.Contains(index, StringComparison.Ordinal));
        if (index == "ix_job_due")
        {
            // The kind is part of the index condition, not a filter over every pending job.
            Assert.Contains(plan, line => line.Contains("Index Cond", StringComparison.Ordinal) && line.Contains("kind", StringComparison.Ordinal));
        }
    }
}
