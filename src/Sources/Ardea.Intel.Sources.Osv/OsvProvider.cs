using System.IO.Compression;
using System.Net;
using System.Runtime.CompilerServices;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sources.Formats.Osv;

namespace Ardea.Intel.Sources.Osv;

/// <summary>
/// OSV.dev for the six package ecosystems of the MVP (master plan 5.6, 16; task 1.12): npm, NuGet,
/// Packagist, PyPI, Maven and Go, one cursor position per ecosystem. Everything comes from the public
/// bucket <c>osv-vulnerabilities</c>, never from the query API, which would send an organisation's
/// dependencies to a foreign service.
/// <list type="bullet">
/// <item><b>Incremental:</b> the ecosystem's <c>modified_id.csv</c> (every record with its last change,
/// newest first; 88 KB for NuGet up to 8.8 MB for npm, and nothing at all while its ETag holds) is read
/// only down to the cursor. The changed records are fetched one by one, oldest first, with a checkpoint
/// every <see cref="CheckpointEvery"/> records.</item>
/// <item><b>First start, or more changes than <see cref="ArchiveThreshold"/>:</b> the ecosystem's
/// <c>all.zip</c> (2.8 MB for NuGet up to 222 MB for npm) on the working volume, of which only records
/// changed since the start of the window are kept. The position then stands at the newest change in the
/// archive.</item>
/// <item><b>Single:</b> one record by id from <c>api.osv.dev/v1/vulns/{id}</c>; it sends nothing but the id.</item>
/// </list>
/// Records of the OpenSSF malicious-packages feed (<c>MAL-</c>) are skipped: they report malicious
/// package versions, not vulnerabilities, and make up 96 % of npm's list (decision in ADR 031).
/// </summary>
public sealed class OsvProvider : ISourceProvider
{
    public const string Key = "osv";
    internal const string BucketUrl = "https://osv-vulnerabilities.storage.googleapis.com/";
    internal const string ApiUrl = "https://api.osv.dev/v1/vulns/";

    public SourceDescriptor Descriptor { get; } = new()
    {
        Key = Key,
        Name = "OSV (npm, NuGet, Packagist, PyPI, Maven, Go)",
        Kind = SourceKind.Record,
        Operator = "Open Source Vulnerabilities (OSV), Google",
        OperatorKind = OperatorKind.Recognized,
        DefaultInterval = TimeSpan.FromHours(1),
        MinimumInterval = TimeSpan.FromMinutes(15),
        AllowedHosts = ["osv-vulnerabilities.storage.googleapis.com", "api.osv.dev"],
        Attribution = "Vulnerability data from OSV.dev (Google, OpenSSF), normalised by Ardea. Records of the GitHub Advisory Database "
            + "(GitHub, Inc.), the PyPA Advisory Database, the Go Vulnerability Database and OSS-Fuzz under CC BY 4.0 "
            + "(https://creativecommons.org/licenses/by/4.0/); of the Drupal Advisory Database under the MIT licence; "
            + "of the Global Security Database under CC0 1.0.",
        // One request per changed record; the change lists and archives are one request each.
        RequestsPerMinute = 600,
        // npm's change list is 8.8 MB and grows with the malicious-packages feed; it is read only down to the cursor.
        MaxResponseBytes = 64L * 1024 * 1024,
        // npm's archive is 222 MB, almost all of it malicious-package reports.
        MaxBackfillBytes = 2L * 1024 * 1024 * 1024,
        ParserVersion = OsvRecordReader.ParserVersion,
    };

    /// <summary>More pending changes than this in one ecosystem, and its archive is cheaper than single requests.</summary>
    internal int ArchiveThreshold { get; init; } = 2000;

    internal int CheckpointEvery { get; init; } = 200;

    public NormalizationResult Normalize(RawRecord record) => OsvRecordReader.Read(record.Content);

    public IAsyncEnumerable<FetchItem> FetchAsync(SyncContext context, CancellationToken cancellationToken) =>
        context.Mode == SyncMode.Single ? FetchSingleAsync(context, cancellationToken) : FetchEcosystemsAsync(context, cancellationToken);

    /// <summary>Malicious-package reports (OpenSSF) are not vulnerabilities; see the class remarks.</summary>
    internal static bool IsSkipped(string id) => id.StartsWith("MAL-", StringComparison.OrdinalIgnoreCase);

    internal static RawRecord Record(string id, string json)
    {
        var metadata = OsvRecordReader.Metadata(json);
        return new RawRecord(id, json, "application/json")
        {
            Url = new Uri("https://osv.dev/vulnerability/" + Uri.EscapeDataString(id)),
            PublishedAt = metadata?.Published,
            ModifiedAt = metadata?.Modified,
        };
    }

    private static async IAsyncEnumerable<FetchItem> FetchSingleAsync(SyncContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = context.SingleId ?? throw new InvalidOperationException("Single mode needs an OSV id.");
        if (!OsvId.IsValid(id))
        {
            throw new ArgumentException($"'{id}' is not an OSV id of a package advisory.", nameof(context));
        }

        var json = await context.Http.GetStringAsync(new Uri(ApiUrl + Uri.EscapeDataString(id)), cancellationToken);
        yield return Record(id, json);
    }

    private async IAsyncEnumerable<FetchItem> FetchEcosystemsAsync(SyncContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cursor = OsvCursor.Parse(context.Cursor) ?? new OsvCursor(context.BackfillFrom, new Dictionary<string, EcosystemCursor>());
        if (cursor.From is null && context.BackfillFrom is not null)
        {
            cursor = cursor with { From = context.BackfillFrom };
        }

        foreach (var ecosystem in OsvEcosystems.Mvp)
        {
            var steps = cursor.Ecosystems.TryGetValue(ecosystem, out var position)
                ? FetchChangesAsync(context, ecosystem, position, cursor, cancellationToken)
                : FetchArchiveAsync(context, ecosystem, cursor.From ?? DateTimeOffset.MinValue, cursor, cancellationToken);
            await foreach (var (item, next) in steps)
            {
                if (next is not null)
                {
                    cursor = next;
                }

                yield return item;
            }
        }
    }

    /// <summary>The changes of one ecosystem after its position, from the change list.</summary>
    private async IAsyncEnumerable<(FetchItem Item, OsvCursor? Cursor)> FetchChangesAsync(
        SyncContext context, string ecosystem, EcosystemCursor position, OsvCursor cursor, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ChangeList changes;
        string validators;
        using (var response = await context.GetIfChangedAsync(new Uri($"{BucketUrl}{ecosystem}/modified_id.csv"), HttpValidators.Parse(position.Validators), cancellationToken))
        {
            if (response.NotModified)
            {
                yield break;
            }

            await using var stream = await response.Response!.Content.ReadAsStreamAsync(cancellationToken);
            changes = await ChangeList.ReadAsync(stream, position.Through, cancellationToken);
            validators = response.Validators.ToString();
        }

        var pending = changes.Pending.Where(c => !IsSkipped(c.Id)).ToList();
        if (pending.Count > ArchiveThreshold)
        {
            // After a long pause or a re-import at OSV.dev, one download is cheaper than thousands of requests.
            await foreach (var step in FetchArchiveAsync(context, ecosystem, position.Through, cursor, cancellationToken))
            {
                yield return step;
            }

            yield break;
        }

        for (var i = 0; i < pending.Count; i++)
        {
            var change = pending[i];
            if (await FetchRecordAsync(context, ecosystem, change.Id, cancellationToken) is { } json)
            {
                yield return (Record(change.Id, json), null);
            }

            // A checkpoint only between distinct times: everything up to its time is then stored.
            var last = i == pending.Count - 1;
            if (!last && (i + 1) % CheckpointEvery == 0 && pending[i + 1].Modified > change.Modified)
            {
                cursor = cursor.With(ecosystem, new EcosystemCursor(change.Modified, position.Validators));
                yield return (new Checkpoint(cursor.ToString()), cursor);
            }
        }

        // Also when only skipped records changed: remember the list's validators and newest time.
        cursor = cursor.With(ecosystem, new EcosystemCursor(changes.Newest ?? position.Through, validators));
        yield return (new Checkpoint(cursor.ToString()), cursor);
    }

    private static async Task<string?> FetchRecordAsync(SyncContext context, string ecosystem, string id, CancellationToken cancellationToken)
    {
        using var response = await context.Http.GetAsync(new Uri($"{BucketUrl}{ecosystem}/{Uri.EscapeDataString(id)}.json"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Listed but gone, e.g. a record that moved to another id in between; the next list no longer has it.
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>
    /// The ecosystem's full archive: written once to the working volume (a zip cannot be read without
    /// seeking), its records read one by one and kept when changed at or after <paramref name="from"/>.
    /// </summary>
    private static async IAsyncEnumerable<(FetchItem Item, OsvCursor? Cursor)> FetchArchiveAsync(
        SyncContext context, string ecosystem, DateTimeOffset from, OsvCursor cursor, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var path = Path.Combine(context.WorkDirectory, ecosystem + "-all.zip");
        using (var response = await context.Http.GetAsync(new Uri($"{BucketUrl}{ecosystem}/all.zip"), HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(path);
            await response.Content.CopyToAsync(file, cancellationToken);
        }

        var newest = from;
        using (var archive = ZipFile.OpenRead(path))
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = entry.Name[..^".json".Length];
                if (IsSkipped(id))
                {
                    continue;
                }

                string json;
                await using (var stream = entry.Open())
                using (var reader = new StreamReader(stream))
                {
                    json = await reader.ReadToEndAsync(cancellationToken);
                }

                var metadata = OsvRecordReader.Metadata(json);
                if (metadata is not null && metadata.Modified > newest)
                {
                    newest = metadata.Modified;
                }

                // A record without a readable change date cannot be filtered; it goes on to quarantine.
                if (metadata is null || metadata.Modified >= from)
                {
                    yield return (Record(metadata?.Id ?? id, json), null);
                }
            }
        }

        File.Delete(path);
        cursor = cursor.With(ecosystem, new EcosystemCursor(newest, null));
        yield return (new Checkpoint(cursor.ToString()), cursor);
    }
}

/// <summary>Ids <see cref="OsvProvider"/> fetches singly: OSV ids of package advisories, not CVE ids.</summary>
internal static class OsvId
{
    public static bool IsValid(string id)
    {
        var dash = id.IndexOf('-', StringComparison.Ordinal);
        return dash > 0 && dash < id.Length - 1 && id.Length <= 100
            && id[..dash].All(char.IsAsciiLetterOrDigit) && char.IsAsciiLetter(id[0])
            && id[(dash + 1)..].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or ':')
            && !id.StartsWith("CVE-", StringComparison.OrdinalIgnoreCase);
    }
}
