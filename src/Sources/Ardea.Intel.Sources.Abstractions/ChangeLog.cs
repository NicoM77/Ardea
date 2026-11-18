namespace Ardea.Intel.Sources.Abstractions;

/// <summary>An identifier the source says changed, and when.</summary>
public sealed record ChangedRecord(string ExternalId, DateTimeOffset? ModifiedAt);

/// <summary>
/// Optional for record sources with a change log, such as the CVE Program's <c>deltaLog.json</c>
/// (master plan 6.8, task 1.11): the core compares the log with what it stored and fetches what is
/// missing or stale one by one, so a gap in the deltas never stays a gap.
/// </summary>
public interface IChangeLogSource
{
    /// <summary>Every record the source's log lists as changed since <paramref name="since"/>; duplicates are fine.</summary>
    IAsyncEnumerable<ChangedRecord> ListChangesAsync(SyncContext context, DateTimeOffset since, CancellationToken cancellationToken);
}
