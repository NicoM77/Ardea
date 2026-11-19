using Npgsql;

namespace Ardea.TestSupport;

/// <summary>
/// Reads the plan PostgreSQL chooses for a query written the way the code sends it (master plan 4.4). With
/// sequential scans and full sorts disabled the plan names an index whenever one can serve the query, which proves the
/// query's expressions match the index; <see cref="SortsEverything"/> shows whether the index also gives
/// the order, so that a page of a list reads a page of rows and not the whole list.
/// </summary>
public static class QueryPlans
{
    public static async Task<IReadOnlyList<string>> ExplainAsync(string connectionString, string sql, CancellationToken cancellationToken, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var off = new NpgsqlCommand("SET enable_seqscan = off; SET enable_sort = off", connection))
        {
            await off.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var explain = new NpgsqlCommand("EXPLAIN " + sql, connection);
        foreach (var parameter in parameters)
        {
            explain.Parameters.Add(parameter);
        }

        var plan = new List<string>();
        await using var reader = await explain.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            plan.Add(reader.GetString(0));
        }

        return plan;
    }

    /// <summary>True when a node sorts all rows it receives, as opposed to reading them in index order (an incremental sort only orders ties).</summary>
    public static bool SortsEverything(IReadOnlyList<string> plan) =>
        plan.Any(line => line.TrimStart(' ', '-', '>').StartsWith("Sort  (", StringComparison.Ordinal));
}
