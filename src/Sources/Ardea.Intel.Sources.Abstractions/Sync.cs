namespace Ardea.Intel.Sources.Abstractions;

public enum SyncMode
{
    /// <summary>Continue from the stored cursor.</summary>
    Incremental,

    /// <summary>First start or a long pause: everything published or changed since <see cref="SyncContext.BackfillFrom"/>.</summary>
    Backfill,

    /// <summary>One record by its id, e.g. an entry asked for that is older than the backfill.</summary>
    Single,
}

/// <summary>Limits of one run (master plan 5.2).</summary>
public sealed record SyncLimits(long MaxResponseBytes, TimeSpan MaxDuration, int MaxRecords);

/// <summary>
/// What an adapter gets for a run: the last saved cursor, the platform's HTTP client (timeouts,
/// retries, rate limit, user agent, host allow-list, size limit), the mode, the clock and a
/// working directory for large downloads. No database, no tenant data, no other adapter.
/// </summary>
public sealed class SyncContext
{
    private TimeSpan? _nextSyncHint;

    public required string SourceKey { get; init; }

    /// <summary>The cursor of the last completed checkpoint; <c>null</c> on the first run.</summary>
    public string? Cursor { get; init; }

    public required HttpClient Http { get; init; }

    public required SyncMode Mode { get; init; }

    public DateTimeOffset? BackfillFrom { get; init; }

    public string? SingleId { get; init; }

    public required TimeProvider Clock { get; init; }

    public required SyncLimits Limits { get; init; }

    /// <summary>A directory on the working volume for this run; deleted afterwards.</summary>
    public required string WorkDirectory { get; init; }

    /// <summary>Operator configuration of the source, e.g. an API key.</summary>
    public IReadOnlyDictionary<string, string> Configuration { get; init; } = new Dictionary<string, string>();

    /// <summary>The source asked for a pause (<c>Retry-After</c>, <c>Cache-Control</c>); the core respects it for the next run.</summary>
    public TimeSpan? NextSyncHint => _nextSyncHint;

    /// <summary>
    /// The cursor a signal provider wants kept, e.g. the validators of its file (record providers use
    /// checkpoints instead). The core stores it only after the run succeeded.
    /// </summary>
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

/// <summary>What <see cref="ISourceProvider.FetchAsync"/> yields: raw records, and checkpoints between them.</summary>
public abstract record FetchItem;

/// <summary>
/// One raw record as the source delivered it. The core hashes the canonical content: an unchanged
/// record produces nothing, however often it is delivered.
/// </summary>
public sealed record RawRecord(string ExternalId, string Content, string ContentType) : FetchItem
{
    public Uri? Url { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public DateTimeOffset? ModifiedAt { get; init; }
}

/// <summary>
/// Everything yielded before this checkpoint is complete up to <see cref="Cursor"/>. The core stores
/// those records and the cursor in one transaction; an aborted run resumes from the last checkpoint.
/// </summary>
public sealed record Checkpoint(string Cursor) : FetchItem;
