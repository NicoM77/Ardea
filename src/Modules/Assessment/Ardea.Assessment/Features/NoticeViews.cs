using Ardea.Assessment.Assessing;
using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Features;

internal sealed record NoticeComponentRef(
    Guid Id, string Name, string? Version, string VersionPrecision, string NoticeThreshold, bool Noisy, ActionProjectRef? Project, bool InStack, bool Removed);

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

internal sealed record AnswerNoticeRequest(string? Answer, string? ExpectedState, Guid? ProjectId = null, string? Version = null);

internal sealed record ReopenNoticeRequest(string? ExpectedState);

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
