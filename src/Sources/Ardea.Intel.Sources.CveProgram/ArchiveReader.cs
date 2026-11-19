using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Sources.CveProgram;

/// <summary>Upper bounds for unpacking the full archive (master plan 9.5: unpacking with limits for size and file count).</summary>
/// <param name="MaxInnerBytes">The inner zip written to the working volume; the real one is about 670 MB.</param>
/// <param name="MaxRecordBytes">One record file; the largest real records have a few hundred KB.</param>
/// <param name="MaxEntries">Files in the archive; the real one has about 320,000.</param>
internal sealed record ArchiveLimits(long MaxInnerBytes, int MaxRecordBytes, int MaxEntries)
{
    public static readonly ArchiveLimits Default = new(2L * 1024 * 1024 * 1024, 4 * 1024 * 1024, 2_000_000);
}

/// <summary>
/// Reads the full archive (<c>…_all_CVEs_at_midnight.zip.zip</c>): an outer zip around an inner zip
/// of record files. The inner zip is written once to the working volume (a zip cannot be read
/// without seeking); its records are then read one by one, never unpacked into files. Every step is
/// bounded by <see cref="ArchiveLimits"/>, by what the data unpacks to and not by what its headers claim:
/// an inner zip or an archive over the limit ends the run, a record over it is skipped.
/// </summary>
internal static class ArchiveReader
{
    public static async IAsyncEnumerable<RawRecord> ReadAsync(
        string archivePath, string workDirectory, DateTimeOffset from, ArchiveLimits limits, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var outer = ZipFile.OpenRead(archivePath);
        var inner = outer.Entries.FirstOrDefault(e => e.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (inner is null)
        {
            // Some archives hold the records directly.
            await foreach (var record in ReadEntriesAsync(outer, from, limits, cancellationToken))
            {
                yield return record;
            }

            yield break;
        }

        var innerPath = Path.Combine(workDirectory, "cves.zip");
        await using (var source = inner.Open())
        await using (var target = File.Create(innerPath))
        {
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                written += read;
                if (written > limits.MaxInnerBytes)
                {
                    throw new InvalidDataException($"The inner archive unpacks to more than {limits.MaxInnerBytes} bytes.");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        using var records = ZipFile.OpenRead(innerPath);
        await foreach (var record in ReadEntriesAsync(records, from, limits, cancellationToken))
        {
            yield return record;
        }
    }

    private static async IAsyncEnumerable<RawRecord> ReadEntriesAsync(
        ZipArchive archive, DateTimeOffset from, ArchiveLimits limits, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (archive.Entries.Count > limits.MaxEntries)
        {
            throw new InvalidDataException($"The archive holds more than {limits.MaxEntries} files.");
        }

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = entry.Name;
            if (!name.StartsWith("CVE-", StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The declared size is checked first, the read itself is bounded too: the header may lie.
            if (entry.Length > limits.MaxRecordBytes || await ReadBoundedAsync(entry, limits.MaxRecordBytes, cancellationToken) is not { } json)
            {
                continue;
            }

            var (published, updated) = Metadata(json);
            if ((published ?? DateTimeOffset.MinValue) < from && (updated ?? DateTimeOffset.MinValue) < from)
            {
                continue;
            }

            var id = name[..^".json".Length];
            yield return CveProgramProvider.Record(id, json, null);
        }
    }

    /// <summary>The entry's text, or <c>null</c> when it unpacks to more than <paramref name="limit"/> bytes or cannot be unpacked.</summary>
    private static async Task<string?> ReadBoundedAsync(ZipArchiveEntry entry, int limit, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = entry.Open();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > limit)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            return System.Text.Encoding.UTF8.GetString(bytes.StartsWith(System.Text.Encoding.UTF8.Preamble) ? bytes[System.Text.Encoding.UTF8.Preamble.Length..] : bytes);
        }
        catch (InvalidDataException)
        {
            // A damaged entry, e.g. sizes that do not match its data.
            return null;
        }
    }

    /// <summary>Publication and update dates from <c>cveMetadata</c>, read without building the whole document.</summary>
    public static (DateTimeOffset? Published, DateTimeOffset? Updated) Metadata(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("cveMetadata", out var metadata))
            {
                return (null, null);
            }

            return (Date(metadata, "datePublished"), Date(metadata, "dateUpdated") ?? Date(metadata, "dateRejected"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static DateTimeOffset? Date(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date
            : null;
}
