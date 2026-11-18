using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Sources.Ncsc;

/// <summary>
/// The NCSC Cyber Security Hub (master plan 5.5, decision E10, task 1.9): the public JSON interface of
/// <c>security-hub.ncsc.admin.ch</c>.
/// <list type="bullet">
/// <item>Every run reads the first page of the list (50 posts, one request). A post is fetched only when it is
/// new or its <c>lastChange</c> differs from the one in the cursor.</item>
/// <item>Once a day (and in backfill mode) all pages are read, because the order of the list is not reliable.</item>
/// <item>The interface has no validators; the cursor is the only memory: post number to the <c>lastChange</c>
/// last seen, plus the time of the last full sweep.</item>
/// </list>
/// The operator can switch the source off like any other; the interface is undocumented and may change.
/// </summary>
public sealed class NcscHubProvider : ISourceProvider
{
    public const string Key = "ncsc-hub";
    internal const string Host = "security-hub.ncsc.admin.ch";
    internal const int PageSize = 50;
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);

    private const int CheckpointEvery = 25;
    private const int MaxPages = 200;

    public SourceDescriptor Descriptor { get; } = new()
    {
        Key = Key,
        Name = "NCSC Cyber Security Hub",
        Kind = SourceKind.Advisory,
        Operator = "National Cyber Security Centre (NCSC)",
        OperatorKind = OperatorKind.Authority,
        DefaultInterval = TimeSpan.FromMinutes(30),
        MinimumInterval = TimeSpan.FromMinutes(30),
        AllowedHosts = [Host],
        Attribution = "Advisories of the Cyber Security Hub of the Swiss National Cyber Security Centre (NCSC), https://security-hub.ncsc.admin.ch",
        // First load: one detail request per post (about 340 today); later runs fetch only what changed.
        RequestsPerMinute = 60,
        MaxResponseBytes = 5L * 1024 * 1024,
        ParserVersion = NcscHubReader.ParserVersion,
    };

    internal static string PostUrl(long id) => $"https://{Host}/#/posts/{id.ToString(CultureInfo.InvariantCulture)}";

    private static Uri ListUrl(int pageIndex) =>
        new($"https://{Host}/api/v1/posts/dashboard?pageSize={PageSize}&pageIndex={pageIndex.ToString(CultureInfo.InvariantCulture)}");

    private static Uri DetailUrl(long id) => new($"https://{Host}/api/v1/posts/{id.ToString(CultureInfo.InvariantCulture)}/details");

    public NormalizationResult Normalize(RawRecord record) => NcscHubReader.Read(record.Content);

    public IAsyncEnumerable<FetchItem> FetchAsync(SyncContext context, CancellationToken cancellationToken) =>
        context.Mode == SyncMode.Single ? FetchSingleAsync(context, cancellationToken) : FetchChangesAsync(context, cancellationToken);

    private static async IAsyncEnumerable<FetchItem> FetchSingleAsync(SyncContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!long.TryParse(context.SingleId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            throw new ArgumentException($"'{context.SingleId}' is not a post number.", nameof(context));
        }

        if (await FetchPostAsync(context, new ListedPost(id, null, null), cancellationToken) is { } record)
        {
            yield return record;
        }
    }

    private static async IAsyncEnumerable<FetchItem> FetchChangesAsync(SyncContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cursor = HubCursor.Parse(context.Cursor);
        var now = context.Clock.GetUtcNow();
        var sweep = cursor?.Sweep is not { } last || context.Mode == SyncMode.Backfill || now - last >= SweepInterval;
        var known = new Dictionary<long, string>(cursor?.Posts ?? new Dictionary<long, string>());

        var listed = await ListAsync(context, sweep, cancellationToken);
        var due = listed
            .Where(p => !known.TryGetValue(p.Id, out var seen) || seen != p.LastChange)
            .OrderBy(p => NcscHttp.ParseDate(p.LastChange) ?? DateTimeOffset.MinValue)
            .ThenBy(p => p.Id)
            .ToList();

        var state = new HubCursor(cursor?.Sweep, known);
        var sinceCheckpoint = 0;
        foreach (var post in due)
        {
            var record = await FetchPostAsync(context, post, cancellationToken);
            if (record is not null)
            {
                yield return record;
            }

            // A post that vanished between list and details (404) is remembered too: the next list tells.
            known[post.Id] = post.LastChange ?? string.Empty;
            if (++sinceCheckpoint == CheckpointEvery)
            {
                sinceCheckpoint = 0;
                yield return new Checkpoint(state.ToString());
            }
        }

        if (sweep)
        {
            state = state with { Sweep = now };
        }

        if (sweep || sinceCheckpoint > 0)
        {
            yield return new Checkpoint(state.ToString());
        }
    }

    /// <summary>The first page, and all further pages on a sweep. Fails when the answer does not look like the list.</summary>
    private static async Task<List<ListedPost>> ListAsync(SyncContext context, bool sweep, CancellationToken cancellationToken)
    {
        var posts = new Dictionary<long, ListedPost>();
        var pages = 1;
        for (var index = 0; index < pages; index++)
        {
            var uri = ListUrl(index);
            var json = await NcscHttp.GetJsonAsync(context, uri, cancellationToken)
                ?? throw new InvalidOperationException($"{uri} was not found; the interface may have changed.");
            using var document = NcscHttp.Parse(json, uri);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("count", out var countElement) || !countElement.TryGetInt32(out var count))
            {
                throw new InvalidOperationException("The post list has an unexpected shape; the interface may have changed.");
            }

            if (items.GetArrayLength() == 0 && index == 0 && count > 0)
            {
                throw new InvalidOperationException($"The post list is empty but announces {count} posts.");
            }

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id) || item.String("title") is null)
                {
                    throw new InvalidOperationException("A post in the list has no id or title; the interface may have changed.");
                }

                // Pages overlap or shift while posts change; the last sighting wins.
                posts[id] = new ListedPost(id, item.String("lastChange") ?? item.String("created"), item.String("title"));
            }

            if (index == 0 && sweep)
            {
                var size = root.TryGetProperty("pageSize", out var sizeElement) && sizeElement.TryGetInt32(out var s) && s > 0 ? Math.Min(s, PageSize) : PageSize;
                pages = Math.Min(MaxPages, (count + size - 1) / size);
            }
        }

        return [.. posts.Values];
    }

    private static async Task<RawRecord?> FetchPostAsync(SyncContext context, ListedPost post, CancellationToken cancellationToken)
    {
        var uri = DetailUrl(post.Id);
        var json = await NcscHttp.GetJsonAsync(context, uri, cancellationToken);
        if (json is null)
        {
            return null;
        }

        using var document = NcscHttp.Parse(json, uri);
        var root = document.RootElement;
        if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id) || id != post.Id)
        {
            throw new InvalidOperationException($"{uri} answered with another post; the interface may have changed.");
        }

        return new RawRecord(post.Id.ToString(CultureInfo.InvariantCulture), json, "application/json")
        {
            Url = new Uri(PostUrl(post.Id)),
            PublishedAt = root.Date("created"),
            ModifiedAt = root.Date("lastModified") ?? root.Date("created"),
        };
    }

    private sealed record ListedPost(long Id, string? LastChange, string? Title);
}

/// <summary>
/// The hub cursor: the <c>lastChange</c> last seen per post number and the time of the last complete
/// sweep. It holds at most <see cref="MaxPosts"/> posts (the newest numbers), so it stays small however
/// long the source runs; a post dropped from it is simply fetched once more when it is seen again.
/// </summary>
internal sealed record HubCursor(DateTimeOffset? Sweep, Dictionary<long, string> Posts)
{
    public const int MaxPosts = 2000;

    public static HubCursor? Parse(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var wire = JsonSerializer.Deserialize<Wire>(cursor);
            return wire is null
                ? null
                : new HubCursor(
                    NcscHttp.ParseDate(wire.Sweep),
                    (wire.Posts ?? new Dictionary<string, string>()).Where(p => long.TryParse(p.Key, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                        .ToDictionary(p => long.Parse(p.Key, CultureInfo.InvariantCulture), p => p.Value));
        }
        catch (JsonException)
        {
            // An unreadable cursor means: start over with a full sweep.
            return null;
        }
    }

    public override string ToString() => JsonSerializer.Serialize(new Wire
    {
        Sweep = Sweep?.ToString("O", CultureInfo.InvariantCulture),
        Posts = Posts.OrderByDescending(p => p.Key).Take(MaxPosts).OrderBy(p => p.Key)
            .ToDictionary(p => p.Key.ToString(CultureInfo.InvariantCulture), p => p.Value),
    });

    private sealed class Wire
    {
        [JsonPropertyName("sweep")]
        public string? Sweep { get; set; }

        [JsonPropertyName("posts")]
        public Dictionary<string, string>? Posts { get; set; }
    }
}
