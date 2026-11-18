namespace Ardea.Intel.Contracts;

/// <summary>
/// Told when an entry changed materially (master plan 6.4: only changes that can alter an
/// assessment). The Assessment module subscribes to start its candidate search; Intel knows nobody.
/// </summary>
public interface IEntryChangeSubscriber
{
    Task OnEntryChangedAsync(EntryChanged change, CancellationToken cancellationToken);
}

/// <param name="EntryId">The entry that changed.</param>
/// <param name="Revision">Its revision after the change.</param>
/// <param name="ChangeKinds">What changed: created, state, severity, affected, exploitation, fix, authority, epss.</param>
public sealed record EntryChanged(Guid EntryId, int Revision, IReadOnlyList<string> ChangeKinds);
