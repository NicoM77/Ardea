using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Sources.CveProgram;

/// <summary>
/// Reads the full archive (<c>…_all_CVEs_at_midnight.zip.zip</c>): an outer zip around an inner zip
/// of record files. The inner zip is written once to the working volume (a zip cannot be read
/// without seeking); its records are then read one by one, never unpacked into files.
/// </summary>
internal static class ArchiveReader
{
    public static async IAsyncEnumerable<RawRecord> ReadAsync(string archivePath, string workDirectory, DateTimeOffset from, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var outer = ZipFile.OpenRead(archivePath);
        var inner = outer.Entries.FirstOrDefault(e => e.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (inner is null)
        {
            // Some archives hold the records directly.
            await foreach (var record in ReadEntriesAsync(outer, from, cancellationToken))
            {
                yield return record;
            }

            yield break;
        }

        var innerPath = Path.Combine(workDirectory, "cves.zip");
        await using (var source = inner.Open())
        await using (var target = File.Create(innerPath))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        using var records = ZipFile.OpenRead(innerPath);
        await foreach (var record in ReadEntriesAsync(records, from, cancellationToken))
        {
            yield return record;
        }
    }

    private static async IAsyncEnumerable<RawRecord> ReadEntriesAsync(ZipArchive archive, DateTimeOffset from, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = entry.Name;
            if (!name.StartsWith("CVE-", StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string json;
            await using (var stream = entry.Open())
            using (var reader = new StreamReader(stream))
            {
                json = await reader.ReadToEndAsync(cancellationToken);
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
