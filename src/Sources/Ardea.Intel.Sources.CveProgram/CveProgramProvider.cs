using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ardea.Intel.Sources.Abstractions;
using Ardea.Intel.Sources.Formats.CveJson5;

namespace Ardea.Intel.Sources.CveProgram;

/// <summary>
/// The CVE Program (master plan 5.5, task 1.4).
/// <list type="bullet">
/// <item><b>Incremental:</b> the change log <c>deltaLog.json</c> (30 days, newest first; about 1.8 MB with
/// gzip, and nothing at all while its ETag holds). Every log entry newer than the cursor is processed
/// oldest first; every changed record is fetched once per run; a checkpoint follows each entry.</item>
/// <item><b>First start or a pause longer than the log:</b> the daily full archive, streamed to the
/// working volume and read without unpacking it to many files; only records published or changed in
/// the backfill window are kept. The cursor then stands at the archive's midnight, and the next run
/// continues from the change log.</item>
/// <item><b>Single:</b> one record from the CVE Services API.</item>
/// </list>
/// </summary>
public sealed class CveProgramProvider : ISourceProvider, IChangeLogSource
{
    public const string Key = "cve-program";
    internal const string DeltaLogUrl = "https://raw.githubusercontent.com/CVEProject/cvelistV5/main/cves/deltaLog.json";
    internal const string ReleasesUrl = "https://api.github.com/repos/CVEProject/cvelistV5/releases?per_page=5";
    internal const string SingleUrl = "https://cveawg.mitre.org/api/cve/";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Bounds for unpacking the full archive; tests set small ones.</summary>
    internal ArchiveLimits ArchiveLimits { get; init; } = ArchiveLimits.Default;

    public SourceDescriptor Descriptor { get; } = new()
    {
        Key = Key,
        Name = "CVE Program",
        Kind = SourceKind.Record,
        Operator = "CVE Program",
        OperatorKind = OperatorKind.Mandated,
        DefaultInterval = TimeSpan.FromHours(1),
        MinimumInterval = TimeSpan.FromMinutes(15),
        AllowedHosts =
        [
            "raw.githubusercontent.com",
            "api.github.com",
            "github.com",
            "objects.githubusercontent.com",
            "release-assets.githubusercontent.com",
            "cveawg.mitre.org",
        ],
        Attribution = "CVE® records © The MITRE Corporation, used under the CVE Program Terms of Use.",
        // A changed record per request; the change log itself is one request.
        RequestsPerMinute = 600,
        // The change log is about 31 MB unpacked.
        MaxResponseBytes = 100L * 1024 * 1024,
        // The full archive is about 670 MB.
        MaxBackfillBytes = 2L * 1024 * 1024 * 1024,
        ParserVersion = CveRecordReader.ParserVersion,
    };

    public NormalizationResult Normalize(RawRecord record) => CveRecordReader.Read(record.Content);

    public IAsyncEnumerable<FetchItem> FetchAsync(SyncContext context, CancellationToken cancellationToken)
    {
        if (context.Mode == SyncMode.Single)
        {
            return FetchSingleAsync(context, cancellationToken);
        }

        var cursor = CveCursor.Parse(context.Cursor);
        return cursor is null ? FetchArchiveAsync(context, cancellationToken) : FetchDeltasAsync(context, cursor, cancellationToken);
    }

    /// <summary>The 30-day change log as the reference for the completeness check (master plan 6.8).</summary>
    public async IAsyncEnumerable<ChangedRecord> ListChangesAsync(SyncContext context, DateTimeOffset since, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var log = await context.Http.GetFromJsonAsync<List<DeltaEntry>>(new Uri(DeltaLogUrl), Json, cancellationToken) ?? [];
        foreach (var entry in log.Where(e => e.FetchTime >= since))
        {
            foreach (var change in (entry.New ?? []).Concat(entry.Updated ?? []))
            {
                if (CveId.IsValid(change.CveId))
                {
                    yield return new ChangedRecord(change.CveId, change.DateUpdated ?? entry.FetchTime);
                }
            }
        }
    }

    private async IAsyncEnumerable<FetchItem> FetchSingleAsync(SyncContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = context.SingleId ?? throw new InvalidOperationException("Single mode needs a CVE id.");
        if (!CveId.IsValid(id))
        {
            throw new ArgumentException($"'{id}' is not a CVE id.", nameof(context));
        }

        var json = await context.Http.GetStringAsync(new Uri(SingleUrl + Uri.EscapeDataString(id)), cancellationToken);
        yield return Record(id, json, null);
    }

    private async IAsyncEnumerable<FetchItem> FetchDeltasAsync(SyncContext context, CveCursor cursor, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var response = await context.GetIfChangedAsync(new Uri(DeltaLogUrl), HttpValidators.Parse(cursor.Validators), cancellationToken);
        if (response.NotModified)
        {
            yield break;
        }

        var log = await response.Response!.Content.ReadFromJsonAsync<List<DeltaEntry>>(Json, cancellationToken) ?? [];
        var plan = DeltaPlan.Create(log, cursor.Through);
        if (plan.GapTooLong)
        {
            // The cursor is older than the log reaches: only the full archive closes the gap.
            await foreach (var item in FetchArchiveAsync(context, cancellationToken))
            {
                yield return item;
            }

            yield break;
        }

        var validators = response.Validators.ToString();
        foreach (var step in plan.Steps)
        {
            foreach (var change in step.Changes)
            {
                var json = await FetchRecordAsync(context, change, cancellationToken);
                if (json is not null)
                {
                    yield return Record(change.CveId, json, change.GithubLink);
                }
            }

            yield return new Checkpoint(new CveCursor(step.FetchTime, step.IsNewest ? validators : cursor.Validators).ToString());
        }

        if (plan.Steps.Count == 0)
        {
            // Nothing new, but remember the log's validators for the next conditional request.
            yield return new Checkpoint(new CveCursor(cursor.Through, validators).ToString());
        }
    }

    private static async Task<string?> FetchRecordAsync(SyncContext context, DeltaChange change, CancellationToken cancellationToken)
    {
        var url = change.GithubLink is { } link && Uri.TryCreate(link, UriKind.Absolute, out var uri) ? uri : new Uri(SingleUrl + Uri.EscapeDataString(change.CveId));
        using var response = await context.Http.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Listed in the log but gone from the repository; the completeness check reports it.
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async IAsyncEnumerable<FetchItem> FetchArchiveAsync(SyncContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var release = await LatestArchiveAsync(context, cancellationToken);
        var archive = Path.Combine(context.WorkDirectory, "all-cves.zip.zip");
        using (var response = await context.Http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(archive);
            await response.Content.CopyToAsync(file, cancellationToken);
        }

        var from = context.BackfillFrom ?? DateTimeOffset.MinValue;
        await foreach (var item in ArchiveReader.ReadAsync(archive, context.WorkDirectory, from, ArchiveLimits, cancellationToken))
        {
            yield return item;
        }

        yield return new Checkpoint(new CveCursor(release.Midnight, null).ToString());
    }

    /// <summary>The newest release with a full archive; its name carries the midnight it was taken at.</summary>
    private static async Task<(Uri Url, DateTimeOffset Midnight)> LatestArchiveAsync(SyncContext context, CancellationToken cancellationToken)
    {
        var releases = await context.Http.GetFromJsonAsync<List<Release>>(new Uri(ReleasesUrl), Json, cancellationToken) ?? [];
        foreach (var asset in releases.SelectMany(r => r.Assets ?? []))
        {
            if (asset.Name.EndsWith("_all_CVEs_at_midnight.zip.zip", StringComparison.Ordinal)
                && DateOnly.TryParseExact(asset.Name[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                return (new Uri(asset.BrowserDownloadUrl), new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
            }
        }

        throw new InvalidOperationException("No release of cvelistV5 offers a full archive.");
    }

    internal static RawRecord Record(string cveId, string json, string? url) => new(cveId, json, "application/json")
    {
        Url = url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : new Uri("https://www.cve.org/CVERecord?id=" + cveId),
        PublishedAt = ArchiveReader.Metadata(json).Published,
        ModifiedAt = ArchiveReader.Metadata(json).Updated,
    };

    private sealed record Release([property: JsonPropertyName("assets")] List<ReleaseAsset>? Assets);

    private sealed record ReleaseAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}

internal static class CveId
{
    public static bool IsValid(string id) =>
        id.StartsWith("CVE-", StringComparison.Ordinal) && id.Length is >= 13 and <= 20
        && id[4..8].All(char.IsAsciiDigit) && id[8] == '-' && id[9..].All(char.IsAsciiDigit);
}
