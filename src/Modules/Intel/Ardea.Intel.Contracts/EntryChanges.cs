namespace Ardea.Intel.Contracts;

public interface IEntryChangeSubscriber
{
    Task OnEntryChangedAsync(EntryChanged change, CancellationToken cancellationToken);
}

public sealed record EntryChanged(Guid EntryId, int Revision, IReadOnlyList<string> ChangeKinds);
