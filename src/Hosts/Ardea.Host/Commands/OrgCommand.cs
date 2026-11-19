using System.Text.Json;
using Ardea.Platform.Persistence;
using Npgsql;

namespace Ardea.Host.Commands;

/// <summary>
/// <c>ardea org export &lt;slug&gt; [--out file]</c> and <c>ardea org delete &lt;slug&gt; --confirm &lt;slug&gt;</c>
/// (master plan 9.1, 9.2, 12.8; task 6.6): an organisation's data as JSON, or gone. They run with the
/// owner role, which row-level security does not limit, and find the organisation's tables in the database
/// catalogue (every table with <c>organization_id</c>), so tables of later modules are covered without
/// being listed. Encrypted columns (<c>*_enc</c>) are never exported.
/// </summary>
internal static class OrgCommand
{
    public const string Format = "ardea-organization-export";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args is not [("export" or "delete") and var verb, var slug, ..])
        {
            await Console.Error.WriteLineAsync("Usage: ardea org export <slug> [--out file] | ardea org delete <slug> --confirm <slug>");
            return 64;
        }

        var configuration = HostConfiguration.Load();
        var check = HostConfiguration.CheckMigration(configuration);
        if (!check.IsValid)
        {
            return HostConfiguration.Fail(check);
        }

        await using var owner = new NpgsqlConnection(DatabaseOptions.FromConfiguration(configuration).OwnerConnectionString("ardea-org"));
        await owner.OpenAsync();
        if (verb == "export")
        {
            var outIndex = Array.IndexOf(args, "--out");
            await using var output = outIndex >= 0 && outIndex + 1 < args.Length ? File.CreateText(args[outIndex + 1]) : Console.Out;
            return await ExportAsync(owner, slug, output, CancellationToken.None) ? 0 : 1;
        }

        var confirmIndex = Array.IndexOf(args, "--confirm");
        if (confirmIndex < 0 || confirmIndex + 1 >= args.Length || args[confirmIndex + 1] != slug)
        {
            await Console.Error.WriteLineAsync($"Deleting removes every project, finding and setting of {slug} for good. Repeat the slug: --confirm {slug}");
            return 64;
        }

        var deleted = await DeleteAsync(owner, slug, CancellationToken.None);
        if (deleted is null)
        {
            await Console.Error.WriteLineAsync($"No organisation {slug}.");
            return 1;
        }

        Console.WriteLine($"Deleted {slug}: {string.Join(", ", deleted.Where(d => d.Value > 0).Select(d => $"{d.Key} {d.Value}"))}.");
        return 0;
    }

    /// <summary>Writes the organisation as one JSON document; <c>false</c> when there is no such organisation.</summary>
    public static async Task<bool> ExportAsync(NpgsqlConnection owner, string slug, TextWriter output, CancellationToken cancellationToken)
    {
        if (await OrganizationIdAsync(owner, slug, cancellationToken) is not { } organizationId)
        {
            await Console.Error.WriteLineAsync($"No organisation {slug}.");
            return false;
        }

        await using var json = new Utf8JsonWriter(new TextWriterStream(output), new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteString("format", Format);
        json.WriteNumber("version", 1);
        json.WriteString("exportedAt", DateTimeOffset.UtcNow);
        json.WriteString("organizationId", organizationId);
        json.WritePropertyName("organization");
        await WriteRowsAsync(owner, "iam.organization", "id", organizationId, [], json, single: true, cancellationToken);

        // Members' accounts are global; the export names them, never their credentials.
        json.WritePropertyName("members");
        await WriteQueryAsync(owner, """
            SELECT coalesce(json_agg(json_build_object('userId', u.id, 'email', u.email, 'displayName', u.display_name, 'role', m.role, 'joinedAt', m.created_at) ORDER BY u.email), '[]')
            FROM iam.membership m JOIN iam."user" u ON u.id = m.user_id WHERE m.organization_id = $1
            """, organizationId, json, cancellationToken);

        json.WriteStartObject("tables");
        foreach (var (table, encrypted) in await OrganizationTablesAsync(owner, cancellationToken))
        {
            json.WritePropertyName(table);
            await WriteRowsAsync(owner, table, "organization_id", organizationId, encrypted, json, single: false, cancellationToken);
        }

        json.WriteEndObject();
        json.WriteEndObject();
        await json.FlushAsync(cancellationToken);
        await output.FlushAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Removes every row of the organisation in one transaction, children before parents, and records an
    /// instance event; <c>null</c> when there is no such organisation. User accounts stay: they are global.
    /// </summary>
    public static async Task<Dictionary<string, int>?> DeleteAsync(NpgsqlConnection owner, string slug, CancellationToken cancellationToken)
    {
        if (await OrganizationIdAsync(owner, slug, cancellationToken) is not { } organizationId)
        {
            return null;
        }

        await using var transaction = await owner.BeginTransactionAsync(cancellationToken);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var remaining = (await OrganizationTablesAsync(owner, cancellationToken)).Select(t => t.Table).ToList();

        // Foreign keys between organisation tables decide the order; a table whose rows are still
        // referenced is tried again in the next round.
        for (var round = 0; remaining.Count > 0; round++)
        {
            if (round > remaining.Count + 5)
            {
                throw new InvalidOperationException($"Could not delete in any order: {string.Join(", ", remaining)}.");
            }

            foreach (var table in remaining.ToList())
            {
                await using var savepoint = new NpgsqlCommand("SAVEPOINT org_delete", owner, transaction);
                await savepoint.ExecuteNonQueryAsync(cancellationToken);
                try
                {
                    await using var delete = new NpgsqlCommand($"DELETE FROM {table} WHERE organization_id = $1", owner, transaction);
                    delete.Parameters.Add(new NpgsqlParameter { Value = organizationId });
                    counts[table] = counts.GetValueOrDefault(table) + await delete.ExecuteNonQueryAsync(cancellationToken);
                    remaining.Remove(table);
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
                {
                    await using var rollback = new NpgsqlCommand("ROLLBACK TO SAVEPOINT org_delete", owner, transaction);
                    await rollback.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        await using (var organization = new NpgsqlCommand("DELETE FROM iam.organization WHERE id = $1", owner, transaction))
        {
            organization.Parameters.Add(new NpgsqlParameter { Value = organizationId });
            counts["iam.organization"] = await organization.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var audit = new NpgsqlCommand(
            "INSERT INTO platform.audit_event (organization_id, action, subject_type, subject_id, data, origin) VALUES (NULL, 'instance.organization_deleted', 'organization', $1, $2, 'command:org delete')",
            owner, transaction))
        {
            audit.Parameters.Add(new NpgsqlParameter { Value = organizationId.ToString() });
            audit.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(new { slug, rows = counts }), NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb });
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return counts;
    }

    /// <summary>Every table that carries <c>organization_id</c>, with its encrypted columns.</summary>
    private static async Task<List<(string Table, string[] Encrypted)>> OrganizationTablesAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT quote_ident(c.table_schema) || '.' || quote_ident(c.table_name),
                   coalesce((SELECT array_agg(e.column_name::text) FROM information_schema.columns e
                             WHERE e.table_schema = c.table_schema AND e.table_name = c.table_name AND e.column_name LIKE '%\_enc'), '{}')
            FROM information_schema.columns c JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
            WHERE c.column_name = 'organization_id' AND t.table_type = 'BASE TABLE'
              AND c.table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY 1
            """, owner);
        var tables = new List<(string, string[])>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add((reader.GetString(0), reader.GetFieldValue<string[]>(1)));
        }

        return tables;
    }

    private static async Task<Guid?> OrganizationIdAsync(NpgsqlConnection owner, string slug, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT id FROM iam.organization WHERE slug = $1", owner);
        command.Parameters.Add(new NpgsqlParameter { Value = slug });
        return await command.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    private static async Task WriteRowsAsync(
        NpgsqlConnection owner, string table, string column, Guid organizationId, string[] encrypted, Utf8JsonWriter json, bool single, CancellationToken cancellationToken)
    {
        // Encrypted values are useless outside this instance and must not leave it (master plan 9.4).
        var row = encrypted.Length == 0 ? "to_jsonb(t)" : $"to_jsonb(t) - ARRAY[{string.Join(", ", encrypted.Select(e => $"'{e}'"))}]";
        var sql = single
            ? $"SELECT {row}::text FROM {table} t WHERE {column} = $1"
            : $"SELECT coalesce(jsonb_agg({row}), '[]')::text FROM {table} t WHERE {column} = $1";
        await WriteQueryAsync(owner, sql, organizationId, json, cancellationToken);
    }

    private static async Task WriteQueryAsync(NpgsqlConnection owner, string sql, Guid organizationId, Utf8JsonWriter json, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, owner);
        command.Parameters.Add(new NpgsqlParameter { Value = organizationId });
        var text = (await command.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "null";
        using var document = JsonDocument.Parse(text);
        document.WriteTo(json);
    }

    /// <summary>Lets the JSON writer write into a text writer such as the console.</summary>
    private sealed class TextWriterStream(TextWriter writer) : Stream
    {
        // Keeps a character split across two writes together.
        private readonly System.Text.Decoder _decoder = System.Text.Encoding.UTF8.GetDecoder();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => writer.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            var chars = new char[_decoder.GetCharCount(buffer, offset, count)];
            var written = _decoder.GetChars(buffer, offset, count, chars, 0);
            writer.Write(chars, 0, written);
        }
    }
}
