using Ardea.Assessment.Persistence;
using Ardea.Intel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Assessing;

/// <summary>
/// What one assessment of an entry found on the coarse path (ADR 047), within what it looked at.
/// </summary>
/// <param name="Scope">Every component the assessment looked at; a notice keeps the components outside it as they were.</param>
/// <param name="Hit">Coarse components the entry hits strongly, within their line.</param>
/// <param name="Raising">Of them, the ones whose threshold the entry meets.</param>
/// <param name="Quiet">A reverse search: raised notices are announced once for all, not one by one (master plan 7.7).</param>
internal sealed record CoarseHits(IReadOnlySet<Guid> Scope, IReadOnlySet<Guid> Hit, IReadOnlySet<Guid> Raising, bool Quiet);

/// <summary>
/// Keeps the organisation's notice of an entry in step with the coarse hits (master plan 17.3; ADR 044, ADR 046, ADR 047).
/// Runs under the alert's lock, in the unit of work of the assessment.
/// <list type="bullet">
/// <item>A notice is raised when a coarse component's threshold is met for the first time; it names the components that
/// raised it. Once raised it stays while one of them is still hit, whatever later changes of the entry do to the score.</item>
/// <item>An open notice no component names any more is withdrawn: they got an exact version and the precise path took
/// over, or they left the profile, or the entry no longer names the product. It is raised anew when a threshold is met again.</item>
/// <item>An answered «doesn't affect us» comes back once (E22): when the entry became actively exploited, or was added to KEV,
/// after it was answered, while a component still names it.</item>
/// </list>
/// </summary>
internal sealed class Notices(AssessDbContext db, History history, TimeProvider time)
{
    private readonly List<Guid> _quietlyRaised = [];

    /// <summary>The notices a reverse search raised in this unit of work, for its single summary.</summary>
    public IReadOnlyList<Guid> QuietlyRaised => _quietlyRaised;

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
                IsBackfill = hits.Quiet,
                RaisedAt = now,
                ChangedAt = now,
            };
            db.Notices.Add(notice);
            Raised(notice, entry, hits.Quiet);
            return;
        }

        // The reason, within what this assessment looked at: the components still hit, and those that raise it now.
        var ids = notice.ComponentIds.Where(id => !hits.Scope.Contains(id) || hits.Hit.Contains(id)).Union(hits.Raising).Order().ToArray();
        var before = (notice.State, notice.EntryKey, notice.CvssScore, notice.Exploitation, Ids: notice.ComponentIds);
        notice.EntryKey = entry.Key;
        notice.CvssScore = entry.CvssScore;
        notice.Exploitation = entry.Exploitation;
        switch (notice.State)
        {
            case Notice.Open when ids.Length == 0:
                // The last reason is kept: what the notice was about.
                notice.State = Notice.Withdrawn;
                history.Add(EventKinds.NoticeWithdrawn, new { entry = entry.Key, components = notice.ComponentIds }, notice: notice.Id);
                break;
            case Notice.Withdrawn when hits.Raising.Count > 0:
                // Raised anew by what raises it now: the components named before left the coarse path or the profile.
                notice.State = Notice.Open;
                notice.ComponentIds = [.. hits.Raising.Order()];
                notice.IsBackfill = hits.Quiet;
                notice.RaisedAt = now;
                Raised(notice, entry, hits.Quiet);
                break;
            case Notice.NotAffected when ids.Length > 0 && notice.ReappearedAt is null
                && NoticeRules.ReappearReason(entry, notice.DecidedExploitation, notice.DecidedKev) is { } reason:
                notice.ComponentIds = ids;
                notice.State = Notice.Open;
                notice.ReappearedAt = now;
                notice.ReappearedReason = reason;
                history.Add(EventKinds.NoticeReappeared, new { entry = entry.Key, reason = notice.ReappearedReason, components = ids }, notice: notice.Id);
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
            return;
        }

        history.Add(EventKinds.NoticeRaised, new { entry = entry.Key, components = notice.ComponentIds, entry.CvssScore, entry.Exploitation }, notice: notice.Id);
    }
}
