using Npgsql;

namespace Ardea.TestSupport;

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

    public static bool SortsEverything(IReadOnlyList<string> plan) =>
        plan.Any(line => line.TrimStart(' ', '-', '>').StartsWith("Sort  (", StringComparison.Ordinal));
}
