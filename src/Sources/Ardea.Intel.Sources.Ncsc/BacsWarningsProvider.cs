using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Sources.Ncsc;

/// <summary>
/// Warnings on the website of the Federal Office for Cyber Security (BACS) (master plan 5.5, task 1.9):
/// the JSON the website is built from (<c>/api/collector/en/current-incidents.json</c> and one page per
/// warning). The interface has no validators and is undocumented.
/// <list type="bullet">
/// <item>Every run reads the overview (one request). A page is fetched when its teaser is new or changed,
/// or when the overview's own <c>updatedAt</c> moved; the page's <c>updatedAt</c> then decides whether a
/// record is delivered. Once a day every page is checked.</item>
/// <item>The terms of the website require written consent for reproducing texts: only title, lead, link
/// and ids are kept (<see cref="TermsMode.MetadataOnly"/>).</item>
/// </list>
/// </summary>
public sealed class BacsWarningsProvider : ISourceProvider
{
    public const string Key = "bacs-warnings";
    internal const string Host = "www.bacs.admin.ch";
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);

    private const int MaxTeasers = 200;
    private const string OverviewPath = "en/current-incidents";

    private static readonly Regex HubLink = new(@"security-hub\.ncsc\.admin\.ch/#/posts/(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public SourceDescriptor Descriptor { get; } = new()
    {
        Key = Key,
        Name = "BACS website warnings",
        Kind = SourceKind.Advisory,
        Operator = "Federal Office for Cyber Security (BACS)",
        OperatorKind = OperatorKind.Authority,
        DefaultInterval = TimeSpan.FromMinutes(30),
        MinimumInterval = TimeSpan.FromMinutes(30),
        AllowedHosts = [Host],
        Attribution = "Warnings of the Federal Office for Cyber Security (BACS), https://www.bacs.admin.ch. Only titles, leads and links are kept; the texts belong to the federal authorities.",
        Terms = TermsMode.MetadataOnly,
        RequestsPerMinute = 30,
        MaxResponseBytes = 5L * 1024 * 1024,
        ParserVersion = BacsWarningReader.ParserVersion,
    };

    private static Uri CollectorUrl(string path) => new($"https://{Host}/api/collector/{path.Trim('/')}.json");

    public NormalizationResult Normalize(RawRecord record) => BacsWarningReader.Read(record.Content);

    public async IAsyncEnumerable<FetchItem> FetchAsync(SyncContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (context.Mode == SyncMode.Single)
        {
            // The website has no lookup by id; a single warning is a page by its slug.
            var slug = context.SingleId ?? throw new InvalidOperationException("Single mode needs the page path, e.g. en/warnung-java-2-en.");
            var single = await FetchPageAsync(context, slug, null, cancellationToken)
                ?? throw new InvalidOperationException($"Page {slug} was not found.");
            yield return single.Record;
            yield break;
        }

        var cursor = BacsCursor.Parse(context.Cursor);
        var now = context.Clock.GetUtcNow();
        var sweep = cursor?.Sweep is not { } last || context.Mode == SyncMode.Backfill || now - last >= SweepInterval;
        var previous = cursor?.Docs ?? new Dictionary<string, string>();

        var (pageUpdatedAt, teasers) = await ReadOverviewAsync(context, cancellationToken);
        var pageMoved = cursor?.Page != pageUpdatedAt;
        var docs = new Dictionary<string, string>();
        foreach (var teaser in teasers)
        {
            var fingerprint = teaser.Fingerprint();
            previous.TryGetValue(teaser.Slug, out var seen);
            var seenFingerprint = seen?.Split('|')[0];
            var seenUpdated = seen is { } s && s.Contains('|', StringComparison.Ordinal) ? s[(s.IndexOf('|', StringComparison.Ordinal) + 1)..] : null;

            if (!sweep && !pageMoved && seenFingerprint == fingerprint)
            {
                docs[teaser.Slug] = seen!;
                continue;
            }

            var page = await FetchPageAsync(context, teaser.Slug, teaser, cancellationToken);
            if (page is null)
            {
                // Listed but gone: nothing to deliver, and it is not remembered.
                continue;
            }

            docs[teaser.Slug] = fingerprint + "|" + page.UpdatedAt;
            if (page.UpdatedAt != seenUpdated)
            {
                yield return page.Record;
            }
        }

        var next = new BacsCursor(sweep ? now : cursor?.Sweep, pageUpdatedAt, docs);
        if (next.ToString() != cursor?.ToString())
        {
            yield return new Checkpoint(next.ToString());
        }
    }

    private static async Task<(string? UpdatedAt, List<Teaser> Teasers)> ReadOverviewAsync(SyncContext context, CancellationToken cancellationToken)
    {
        var uri = CollectorUrl(OverviewPath);
        var json = await NcscHttp.GetJsonAsync(context, uri, cancellationToken)
            ?? throw new InvalidOperationException($"{uri} was not found; the website may have changed.");
        using var document = NcscHttp.Parse(json, uri);
        var teasers = new List<Teaser>();
        Walk(document.RootElement, teasers);

        // The overview always lists warnings; none at all means the structure changed.
        if (teasers.Count == 0)
        {
            throw new InvalidOperationException("The overview lists no warnings; the website's structure may have changed.");
        }

        return (document.RootElement.String("updatedAt"), teasers.DistinctBy(t => t.Slug).Take(MaxTeasers).ToList());
    }

    private static void Walk(JsonElement element, List<Teaser> teasers)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.String("identifier") == "teaser-item" && element.TryGetProperty("properties", out var p)
                    && p.String("slug") is { Length: > 0 } slug && p.String("title") is { } title)
                {
                    teasers.Add(new Teaser(slug.Trim('/'), title, p.String("description"), p.TryGetProperty("metaInfo", out var meta) ? meta.String("date") : null));
                    return;
                }

                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, teasers);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, teasers);
                }

                break;
        }
    }

    /// <summary>Fetches a page and reduces it to what the terms allow us to keep.</summary>
    private static async Task<Page?> FetchPageAsync(SyncContext context, string slug, Teaser? teaser, CancellationToken cancellationToken)
    {
        var uri = CollectorUrl(slug);
        var json = await NcscHttp.GetJsonAsync(context, uri, cancellationToken);
        if (json is null)
        {
            return null;
        }

        using var document = NcscHttp.Parse(json, uri);
        var root = document.RootElement;
        var meta = root.TryGetProperty("metadata", out var m) ? m : default;
        if (!root.TryGetProperty("documentId", out var idElement) || !idElement.TryGetInt64(out var documentId)
            || meta.String("groupId") is not { Length: > 0 } groupId
            || (meta.String("title") ?? teaser?.Title) is not { Length: > 0 } title
            || root.String("updatedAt") is not { Length: > 0 } updatedAt)
        {
            throw new InvalidOperationException($"{uri} lacks document id, group id, title or updatedAt; the website may have changed.");
        }

        var lang = root.String("lang") ?? meta.String("lang") ?? "en";
        var pageSlug = meta.String("slug") ?? slug.Split('/').Last();
        var hubPosts = new SortedSet<long>();
        CollectHubPosts(root, hubPosts);

        var warning = new BacsWarning
        {
            DocumentId = documentId,
            GroupId = groupId,
            Lang = lang,
            Url = $"https://{Host}/{lang}/{pageSlug}",
            Title = BacsWarningReader.PlainText(title),
            Summary = BacsWarningReader.PlainText(meta.String("description") ?? meta.String("teaserText") ?? teaser?.Description),
            PublishedAt = meta.String("publishDate") ?? root.String("visiblePublicationDate"),
            UpdatedAt = updatedAt,
            HubPosts = [.. hubPosts],
        };

        var record = new RawRecord(groupId, JsonSerializer.Serialize(warning, BacsWarningReader.Json), "application/json")
        {
            Url = new Uri(warning.Url),
            PublishedAt = NcscHttp.ParseDate(warning.PublishedAt),
            ModifiedAt = NcscHttp.ParseDate(updatedAt),
        };
        return new Page(record, updatedAt);
    }

    private static void CollectHubPosts(JsonElement element, SortedSet<long> posts)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                foreach (Match match in HubLink.Matches(element.GetString()!))
                {
                    posts.Add(long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectHubPosts(property.Value, posts);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectHubPosts(item, posts);
                }

                break;
        }
    }

    private sealed record Page(RawRecord Record, string UpdatedAt);

    private sealed record Teaser(string Slug, string Title, string? Description, string? Date)
    {
        /// <summary>Short and stable: a change of date, title or lead shows without fetching the page.</summary>
        public string Fingerprint() =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Date}\n{Title}\n{Description}")), 0, 6).ToLowerInvariant();
    }
}

/// <summary>
/// The BACS cursor: the overview's <c>updatedAt</c>, per listed page a fingerprint of its teaser and the page's
/// <c>updatedAt</c>, and the time of the last full check. Pages that left the overview drop out, so it is
/// bounded by the overview's length.
/// </summary>
internal sealed record BacsCursor(DateTimeOffset? Sweep, string? Page, Dictionary<string, string> Docs)
{
    public static BacsCursor? Parse(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var wire = JsonSerializer.Deserialize<Wire>(cursor);
            return wire is null ? null : new BacsCursor(NcscHttp.ParseDate(wire.Sweep), wire.Page, wire.Docs ?? new Dictionary<string, string>());
        }
        catch (JsonException)
        {
            // An unreadable cursor means: check everything again.
            return null;
        }
    }

    public override string ToString() => JsonSerializer.Serialize(new Wire
    {
        Sweep = Sweep?.ToString("O", CultureInfo.InvariantCulture),
        Page = Page,
        Docs = new SortedDictionary<string, string>(Docs, StringComparer.Ordinal).ToDictionary(d => d.Key, d => d.Value),
    });

    private sealed class Wire
    {
        [JsonPropertyName("sweep")]
        public string? Sweep { get; set; }

        [JsonPropertyName("page")]
        public string? Page { get; set; }

        [JsonPropertyName("docs")]
        public Dictionary<string, string>? Docs { get; set; }
    }
}
