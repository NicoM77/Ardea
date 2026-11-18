using Ardea.Intel.Sources.Formats.Osv;

namespace Ardea.Intel.Sources.Osv;

internal sealed record Change(DateTimeOffset Modified, string Id);

/// <summary>
/// An ecosystem's <c>modified_id.csv</c>: one line <c>timestamp,id</c> per record, newest first. It is
/// read only down to the cursor, so a run costs the new lines, not the whole list (8.8 MB for npm).
/// </summary>
internal sealed record ChangeList(IReadOnlyList<Change> Pending, DateTimeOffset? Newest)
{
    /// <summary>The changes after <paramref name="through"/>, oldest first, and the time of the newest line.</summary>
    public static async Task<ChangeList> ReadAsync(Stream stream, DateTimeOffset through, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        var pending = new List<Change>();
        DateTimeOffset? newest = null;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var comma = line.IndexOf(',', StringComparison.Ordinal);
            if (comma <= 0 || !OsvTimestamp.TryParse(line[..comma], out var modified))
            {
                // A header or a broken line carries no change.
                continue;
            }

            newest ??= modified;
            if (modified <= through)
            {
                // Newest first: everything below is already stored.
                break;
            }

            var id = line[(comma + 1)..].Trim();
            if (id.Length > 0)
            {
                pending.Add(new Change(modified, id));
            }
        }

        // Stable: lines with the same time keep the list's order.
        return new ChangeList([.. pending.OrderBy(c => c.Modified)], newest);
    }
}
