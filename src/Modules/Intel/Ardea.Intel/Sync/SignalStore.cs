using Ardea.Intel.Sources.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace Ardea.Intel.Sync;

/// <summary>
/// Writes signals in bulk (master plan 4.4, 6.4): a batch goes into a temporary table with COPY and is
/// merged into <c>entry_signal</c> in one statement, which returns only the identifiers whose EPSS
/// class or KEV listing changed — the daily EPSS run touches no other entry. A provider only sets its
/// own fields; the other source's values stay.
/// </summary>
internal static class SignalStore
{
    /// <summary>The EPSS class changes only after the score crossed 50 % by more than five points.</summary>
    public const double HighAbove = 0.55;
    public const double LowBelow = 0.45;

    public static async Task<IReadOnlyList<string>> StoreAsync(NpgsqlConnection connection, IReadOnlyList<Signal> batch, CancellationToken cancellationToken)
    {
        await using (var create = new NpgsqlCommand(
            """
            CREATE TEMP TABLE signal_batch (
                scheme text, value text, kev_listed_on date, kev_url text, kev_ransomware boolean, epss_score double precision, epss_percentile double precision
            ) ON COMMIT DROP
            """, connection))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var import = await connection.BeginBinaryImportAsync(
            "COPY signal_batch (scheme, value, kev_listed_on, kev_url, kev_ransomware, epss_score, epss_percentile) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var signal in batch.DistinctBy(s => (s.Scheme, s.Value)))
            {
                await import.StartRowAsync(cancellationToken);
                await import.WriteAsync(signal.Scheme.ToUpperInvariant(), NpgsqlDbType.Text, cancellationToken);
                await import.WriteAsync(signal.Value, NpgsqlDbType.Text, cancellationToken);
                await WriteNullable(import, signal.KevListedOn, NpgsqlDbType.Date, cancellationToken);
                await WriteNullable(import, signal.KevUrl?.AbsoluteUri, NpgsqlDbType.Text, cancellationToken);
                await WriteNullable(import, signal.KevKnownRansomware, NpgsqlDbType.Boolean, cancellationToken);
                await WriteNullable(import, signal.EpssScore, NpgsqlDbType.Double, cancellationToken);
                await WriteNullable(import, signal.EpssPercentile, NpgsqlDbType.Double, cancellationToken);
            }

            await import.CompleteAsync(cancellationToken);
        }

        await using var merge = new NpgsqlCommand(
            $"""
            WITH previous AS (
                SELECT s.scheme, s.value, s.epss_class, s.kev_listed_on
                FROM intel.entry_signal s JOIN signal_batch b ON b.scheme = s.scheme AND b.value = s.value
            ), merged AS (
                INSERT INTO intel.entry_signal AS s (scheme, value, kev_listed_on, kev_url, kev_known_ransomware, epss_score, epss_percentile, epss_class, updated_at)
                SELECT scheme, value, kev_listed_on, kev_url, kev_ransomware, epss_score, epss_percentile,
                       CASE WHEN epss_score IS NULL THEN NULL WHEN epss_score >= 0.5 THEN 'high' ELSE 'low' END, now()
                FROM signal_batch
                ON CONFLICT (scheme, value) DO UPDATE SET
                    kev_listed_on = coalesce(excluded.kev_listed_on, s.kev_listed_on),
                    kev_url = coalesce(excluded.kev_url, s.kev_url),
                    kev_known_ransomware = coalesce(excluded.kev_known_ransomware, s.kev_known_ransomware),
                    epss_score = coalesce(excluded.epss_score, s.epss_score),
                    epss_percentile = coalesce(excluded.epss_percentile, s.epss_percentile),
                    epss_class = CASE
                        WHEN excluded.epss_score IS NULL THEN s.epss_class
                        WHEN s.epss_class = 'high' THEN CASE WHEN excluded.epss_score < {LowBelow.ToString(System.Globalization.CultureInfo.InvariantCulture)} THEN 'low' ELSE 'high' END
                        WHEN s.epss_class = 'low' THEN CASE WHEN excluded.epss_score > {HighAbove.ToString(System.Globalization.CultureInfo.InvariantCulture)} THEN 'high' ELSE 'low' END
                        ELSE CASE WHEN excluded.epss_score >= 0.5 THEN 'high' ELSE 'low' END END,
                    updated_at = now()
                RETURNING s.scheme, s.value, s.epss_class, s.kev_listed_on
            )
            SELECT m.scheme || ':' || m.value
            FROM merged m LEFT JOIN previous p ON p.scheme = m.scheme AND p.value = m.value
            WHERE (coalesce(p.epss_class, 'low') = 'high') IS DISTINCT FROM (coalesce(m.epss_class, 'low') = 'high')
               OR (p.kev_listed_on IS NULL AND m.kev_listed_on IS NOT NULL)
            """, connection);
        var changed = new List<string>();
        await using var reader = await merge.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            changed.Add(reader.GetString(0));
        }

        return changed;
    }

    private static Task WriteNullable<T>(NpgsqlBinaryImporter import, T? value, NpgsqlDbType type, CancellationToken cancellationToken) =>
        value is null ? import.WriteNullAsync(cancellationToken) : import.WriteAsync(value, type, cancellationToken);
}
