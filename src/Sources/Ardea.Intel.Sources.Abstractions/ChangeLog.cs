namespace Ardea.Intel.Sources.Abstractions;

public sealed record ChangedRecord(string ExternalId, DateTimeOffset? ModifiedAt);

public interface IChangeLogSource
{
    IAsyncEnumerable<ChangedRecord> ListChangesAsync(SyncContext context, DateTimeOffset since, CancellationToken cancellationToken);
}
