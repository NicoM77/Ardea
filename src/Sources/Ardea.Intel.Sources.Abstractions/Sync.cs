namespace Ardea.Intel.Sources.Abstractions;

public enum SyncMode
{
    Incremental,

    Backfill,

    Single,
}

public sealed record SyncLimits(long MaxResponseBytes, TimeSpan MaxDuration, int MaxRecords);

public sealed class SyncContext
{
    private TimeSpan? _nextSyncHint;

    public required string SourceKey { get; init; }

    public string? Cursor { get; init; }

    public required HttpClient Http { get; init; }

    public required SyncMode Mode { get; init; }

    public DateTimeOffset? BackfillFrom { get; init; }

    public string? SingleId { get; init; }

    public required TimeProvider Clock { get; init; }

    public required SyncLimits Limits { get; init; }

    public required string WorkDirectory { get; init; }

    public IReadOnlyDictionary<string, string> Configuration { get; init; } = new Dictionary<string, string>();

    public TimeSpan? NextSyncHint => _nextSyncHint;

    public string? NewCursor { get; private set; }

    public void SaveCursor(string cursor) => NewCursor = cursor;

    public void SuggestNextSync(TimeSpan delay)
    {
        if (delay > TimeSpan.Zero && (_nextSyncHint is null || delay > _nextSyncHint))
        {
            _nextSyncHint = delay;
        }
    }
}

public abstract record FetchItem;

public sealed record RawRecord(string ExternalId, string Content, string ContentType) : FetchItem
{
    public Uri? Url { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public DateTimeOffset? ModifiedAt { get; init; }
}

public sealed record Checkpoint(string Cursor) : FetchItem;
