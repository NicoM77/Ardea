using Ardea.Assessment.Assessing;
using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Features;

/// <summary>A component a notice names: the reason «Your stack names PostgreSQL 17» (master plan 17.3).</summary>
/// <param name="Id">The component.</param>
/// <param name="Name">The product's name.</param>
/// <param name="Version">Its version line, or <c>null</c> without one; an exact version once the precise path took over.</param>
/// <param name="VersionPrecision"><c>none</c>, <c>line</c> or <c>exact</c>.</param>
/// <param name="NoticeThreshold"><c>only_important</c> or <c>everything</c>.</param>
/// <param name="Noisy">The catalogue marks the product as noisy: «only important» means exploited or warned about (ADR 044).</param>
/// <param name="Project">The project it belongs to, the stack being one too; <c>null</c> once removed.</param>
/// <param name="InStack">It is an entry of the organisation's stack.</param>
/// <param name="Removed">It left the profile since.</param>
internal sealed record NoticeComponentRef(
    Guid Id, string Name, string? Version, string VersionPrecision, string NoticeThreshold, bool Noisy, ActionProjectRef? Project, bool InStack, bool Removed);

/// <summary>A notice (master plan 17.3, ADR 047): «could affect you», with the reason and the source's severity and exploitation.</summary>
/// <param name="Id">The notice.</param>
/// <param name="State"><c>open</c>, <c>affects_us</c>, <c>not_affected</c> or <c>withdrawn</c>.</param>
/// <param name="Entry">The entry it asks about.</param>
/// <param name="CvssScore">The entry's CVSS base score; <c>null</c> when the sources give none.</param>
/// <param name="Exploitation"><c>none</c>, <c>poc</c> or <c>active</c>.</param>
/// <param name="RaisedAt">When it was raised, or raised anew after a withdrawal.</param>
/// <param name="IsBackfill">Raised when a stack entry was added or changed, with others at once.</param>
/// <param name="Components">The coarse components that raised it.</param>
/// <param name="DecidedBy">Who answered; kept on a notice that came back once.</param>
/// <param name="DecidedAt">When.</param>
/// <param name="ReminderNumber">The reminder «affects us» opened or joined; <c>null</c> otherwise.</param>
/// <param name="ReappearedAt">When an answered «doesn't affect us» came back, once (E22).</param>
/// <param name="ReappearedReason"><c>exploited</c> or <c>kev</c>.</param>
/// <param name="ChangedAt">When it last changed, for the answer's check.</param>
internal sealed record NoticeView(
    Guid Id,
    string State,
    ActionEntryRef Entry,
    double? CvssScore,
    string Exploitation,
    DateTimeOffset RaisedAt,
    bool IsBackfill,
    IReadOnlyList<NoticeComponentRef> Components,
    Guid? DecidedBy,
    DateTimeOffset? DecidedAt,
    int? ReminderNumber,
    DateTimeOffset? ReappearedAt,
    string? ReappearedReason,
    DateTimeOffset ChangedAt);

/// <summary>
/// An answer to a notice (master plan 17.3). <c>answer</c>: <c>affects_us</c> or <c>not_affected</c>; <c>expectedState</c>: the
/// state the person saw, <c>open</c>. «Affects us» may say where it runs: a project (else the stack entry the notice names)
/// and a version, which is recorded on the project's component of the product and checked precisely (E21).
/// </summary>
internal sealed record AnswerNoticeRequest(string? Answer, string? ExpectedState, Guid? ProjectId = null, string? Version = null);

/// <summary>Takes an answer back (ADR 047: undo is a plain reopen); <c>expectedState</c> is the answer the person saw.</summary>
internal sealed record ReopenNoticeRequest(string? ExpectedState);

/// <summary>Notices as the API shows them, in the caller's unit of work.</summary>
internal sealed class NoticeReader(AssessDbContext db, IProfileQueries profiles, IEntryQueries entries, IProductCatalog catalog)
{
    public async Task<IReadOnlyList<NoticeView>> ViewsAsync(IReadOnlyList<Notice> notices, CancellationToken cancellationToken)
    {
        if (notices.Count == 0)
        {
            return [];
        }

        var titles = (await entries.FindManyAsync([.. notices.Select(n => n.EntryId).Distinct()], cancellationToken)).ToDictionary(e => e.Id, e => e.Title);
        var components = (await profiles.FindComponentsAsync([.. notices.SelectMany(n => n.ComponentIds).Distinct()], cancellationToken)).ToDictionary(c => c.Id);
        var reminderIds = notices.Select(n => n.RemediationId).OfType<Guid>().Distinct().ToArray();
        var numbers = await db.Remediations.AsNoTracking().Where(r => reminderIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Number, cancellationToken);
        var noisy = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in components.Values.Select(c => c.ProductKey).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            if ((await catalog.FindByKeyAsync(key, cancellationToken))?.Noisy == true)
            {
                noisy.Add(key);
            }
        }

        NoticeComponentRef Component(Guid id) => components.TryGetValue(id, out var c)
            ? new NoticeComponentRef(
                c.Id, c.Name, c.Version, c.VersionPrecision, c.NoticeThreshold, c.ProductKey is { } k && noisy.Contains(k),
                new ActionProjectRef(c.ProjectId, c.ProjectName), c.ProjectKind == "organization_stack", Removed: false)
            : new NoticeComponentRef(id, "?", null, "none", NoticeRules.OnlyImportant, false, null, false, Removed: true);

        return [.. notices.Select(n => new NoticeView(
            n.Id, n.State, new ActionEntryRef(n.EntryId, n.EntryKey, titles.GetValueOrDefault(n.EntryId)), n.CvssScore, n.Exploitation, n.RaisedAt, n.IsBackfill,
            [.. n.ComponentIds.Select(Component)], n.DecidedBy, n.DecidedAt,
            n.RemediationId is { } r && numbers.TryGetValue(r, out var number) ? number : null,
            n.ReappearedAt, n.ReappearedReason, n.ChangedAt))];
    }
}
