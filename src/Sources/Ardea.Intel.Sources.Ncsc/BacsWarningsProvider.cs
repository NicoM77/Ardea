using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Sources.Ncsc;

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
        public string Fingerprint() =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Date}\n{Title}\n{Description}")), 0, 6).ToLowerInvariant();
    }
}

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
