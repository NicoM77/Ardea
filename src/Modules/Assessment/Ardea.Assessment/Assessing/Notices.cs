using Ardea.Assessment.Persistence;
using Ardea.Intel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Assessing;

internal sealed record CoarseHits(IReadOnlySet<Guid> Scope, IReadOnlySet<Guid> Hit, IReadOnlySet<Guid> Raising, IReadOnlySet<Guid> HandedOver, bool Quiet)
{
    public bool RaisesQuietly => Quiet && HandedOver.Count == 0;
}

internal sealed class Notices(AssessDbContext db, History history, TimeProvider time)
{
    private readonly List<Guid> _quietlyRaised = [];

    public IReadOnlyList<Guid> QuietlyRaised => _quietlyRaised;

    public int QuietlyRaisedImportant { get; private set; }

    public async Task UpdateAsync(EntryFacts entry, CoarseHits hits, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var notice = await db.Notices.SingleOrDefaultAsync(n => n.EntryId == entry.Id, cancellationToken);
        if (notice is null)
        {
            if (hits.Raising.Count == 0)
            {
                return;
            }

            notice = new Notice
            {
                Id = Guid.CreateVersion7(),
                EntryId = entry.Id,
                EntryKey = entry.Key,
                State = Notice.Open,
                ComponentIds = [.. hits.Raising.Order()],
                CvssScore = entry.CvssScore,
                Exploitation = entry.Exploitation,
                IsBackfill = hits.RaisesQuietly,
                RaisedAt = now,
                ChangedAt = now,
            };
            db.Notices.Add(notice);
            Raised(notice, entry, hits.RaisesQuietly);
            return;
        }

        var ids = notice.ComponentIds.Where(id => !hits.Scope.Contains(id) || hits.Hit.Contains(id)).Union(hits.Raising).Order().ToArray();
        var before = (notice.State, notice.EntryKey, notice.CvssScore, notice.Exploitation, Ids: notice.ComponentIds);
        var raisedNow = false;
        notice.EntryKey = entry.Key;
        notice.CvssScore = entry.CvssScore;
        notice.Exploitation = entry.Exploitation;
        switch (notice.State)
        {
            case Notice.Open when ids.Length == 0:
                notice.State = Notice.Withdrawn;
                history.Add(EventKinds.NoticeWithdrawn, new { entry = entry.Key, components = notice.ComponentIds }, notice: notice.Id);
                break;
            case Notice.Withdrawn when hits.Raising.Count > 0:
                notice.State = Notice.Open;
                notice.ComponentIds = [.. hits.Raising.Order()];
                notice.IsBackfill = hits.RaisesQuietly;
                notice.RaisedAt = now;
                Raised(notice, entry, hits.RaisesQuietly);
                break;
            case Notice.NotAffected when ids.Length > 0 && notice.ReappearedAt is null
                && NoticeRules.ReappearReason(entry, notice.DecidedExploitation, notice.DecidedKev) is { } reason:
                notice.ComponentIds = ids;
                notice.State = Notice.Open;
                notice.ReappearedAt = now;
                notice.ReappearedReason = reason;
                history.Add(EventKinds.NoticeReappeared, new { entry = entry.Key, reason = notice.ReappearedReason, components = ids }, notice: notice.Id);
                break;
            case Notice.Open when notice.IsBackfill && hits.HandedOver.Count > 0:
                notice.ComponentIds = ids;
                notice.IsBackfill = false;
                notice.ChangedAt = now;
                Raised(notice, entry, quiet: false);
                raisedNow = true;
                break;
            case Notice.Withdrawn:
                break;
            default:
                if (ids.Length > 0)
                {
                    notice.ComponentIds = ids;
                }

                break;
        }

        if (before.State == Notice.Open && notice.State == Notice.Open && !raisedNow
            && before.Exploitation != "active" && entry.Exploitation == "active"
            && !db.Events.Local.Any(e => e.NoticeId == notice.Id && e.Kind == EventKinds.NoticeExploited)
            && !await db.Events.AnyAsync(e => e.NoticeId == notice.Id && e.Kind == EventKinds.NoticeExploited, cancellationToken))
        {
            history.Add(EventKinds.NoticeExploited, new { entry = entry.Key, components = notice.ComponentIds, kev = entry.InKev }, notice: notice.Id);
        }

        if (before.State != notice.State || before.EntryKey != notice.EntryKey || before.CvssScore != notice.CvssScore
            || before.Exploitation != notice.Exploitation || !before.Ids.AsSpan().SequenceEqual(notice.ComponentIds))
        {
            notice.ChangedAt = now;
        }
    }

    private void Raised(Notice notice, EntryFacts entry, bool quiet)
    {
        if (quiet)
        {
            _quietlyRaised.Add(notice.Id);
            if (entry.Exploitation == "active" || entry.InKev)
            {
                QuietlyRaisedImportant++;
            }

            return;
        }

        history.Add(EventKinds.NoticeRaised, new { entry = entry.Key, components = notice.ComponentIds, entry.CvssScore, entry.Exploitation }, notice: notice.Id);
    }
}
