using Ardea.Intel.Sources.Formats.Osv;

namespace Ardea.Intel.Sources.Osv;

internal sealed record Change(DateTimeOffset Modified, string Id);

internal sealed record ChangeList(IReadOnlyList<Change> Pending, DateTimeOffset? Newest)
{
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
                continue;
            }

            newest ??= modified;
            if (modified <= through)
            {
                break;
            }

            var id = line[(comma + 1)..].Trim();
            if (id.Length > 0)
            {
                pending.Add(new Change(modified, id));
            }
        }

        return new ChangeList([.. pending.OrderBy(c => c.Modified)], newest);
    }
}
