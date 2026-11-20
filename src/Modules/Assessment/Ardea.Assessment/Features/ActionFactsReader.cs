using System.Text.Json;
using Ardea.Assessment.Assessing;
using Ardea.Assessment.Contracts;
using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Intel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Features;

/// <summary>Events and actions for the notifications (master plan 8.2, 8.5), in the caller's unit of work.</summary>
internal sealed class ActionFactsReader(AssessDbContext db, IProfileQueries profiles, IEntryQueries entries) : IActionFacts
{
    public async Task<IReadOnlyList<ActionEventFacts>> EventsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken)
    {
        if (eventIds.Count == 0)
        {
            return [];
        }

        var ids = eventIds.ToArray();
        var rows = await db.Events.AsNoTracking().Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken);
        // Rows of the assignment and the review state from before ADR 046 announce nothing.
        return [.. rows.Where(e => EventKinds.IsNotifiable(e.Kind) && !LegacyHistory.Hidden(e.Kind, e.Data.RootElement))
            .Select(e => new ActionEventFacts(e.Id, e.Kind, e.At, e.RemediationId, e.ProjectId, e.Actor, LegacyHistory.Shown(e.Data.RootElement), e.NoticeId))];
    }

    public async Task<IReadOnlyList<NoticeFacts>> NoticesAsync(IReadOnlyCollection<Guid> noticeIds, CancellationToken cancellationToken)
    {
        if (noticeIds.Count == 0)
        {
            return [];
        }

        var ids = noticeIds.ToArray();
        var notices = await db.Notices.AsNoTracking().Where(n => ids.Contains(n.Id)).ToListAsync(cancellationToken);
        if (notices.Count == 0)
        {
            return [];
        }

        var facts = (await entries.FindManyAsync([.. notices.Select(n => n.EntryId).Distinct()], cancellationToken)).ToDictionary(e => e.Id);
        var components = (await profiles.FindComponentsAsync([.. notices.SelectMany(n => n.ComponentIds).Distinct()], cancellationToken)).ToDictionary(c => c.Id);
        return [.. notices.Select(n =>
        {
            var entry = facts.GetValueOrDefault(n.EntryId);
            return new NoticeFacts
            {
                Id = n.Id,
                State = n.State,
                EntryKey = n.EntryKey,
                EntryTitle = entry?.Title,
                CvssScore = n.CvssScore,
                Exploitation = n.Exploitation,
                InKev = entry?.InKev ?? false,
                ReappearedReason = n.ReappearedReason,
                // A component removed since is no reason any more; the notice is withdrawn with the next assessment.
                Components = [.. n.ComponentIds.Select(components.GetValueOrDefault).OfType<ProfileComponent>()
                    .Select(c => new NoticeComponentFacts(c.Name, c.Version, c.ProjectId, c.ProjectName, c.ProjectKind == "organization_stack"))],
            };
        })];
    }

    public async Task<IReadOnlyList<OpenReminder>> OpenRemindersAsync(int minLevel, CancellationToken cancellationToken)
    {
        var open = await db.Remediations.AsNoTracking().Where(r => r.Status != "closed" && r.Level >= minLevel)
            .Select(r => new { r.Id, r.Level, r.ProjectId, r.OpenedAt, r.LevelRaisedAt }).ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return [];
        }

        // A reminder a person took back from «done» counts from that moment (ADR 048): the last status change to «open».
        var ids = open.Select(r => r.Id).ToArray();
        var reopened = (await db.Events.AsNoTracking()
                .Where(e => e.Kind == EventKinds.ActionStatusChanged && e.RemediationId != null && ids.Contains(e.RemediationId.Value))
                .Select(e => new { RemediationId = e.RemediationId!.Value, e.At, e.Data })
                .ToListAsync(cancellationToken))
            .Where(e => e.Data.RootElement.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.String && to.GetString() == "open")
            .GroupBy(e => e.RemediationId)
            .ToDictionary(g => g.Key, g => g.Max(e => e.At));
        return [.. open.Select(r =>
        {
            var since = new[] { r.OpenedAt, r.LevelRaisedAt ?? r.OpenedAt, reopened.GetValueOrDefault(r.Id, r.OpenedAt) }.Max();
            return new OpenReminder(r.Id, r.Level, r.ProjectId, since);
        })];
    }

    public async Task<IReadOnlyList<ActionFacts>> ActionsAsync(IReadOnlyCollection<Guid> remediationIds, CancellationToken cancellationToken)
    {
        if (remediationIds.Count == 0)
        {
            return [];
        }

        var ids = remediationIds.ToArray();
        return await FactsAsync(await db.Remediations.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<ActionFacts>> BackfillActionsAsync(Guid projectId, CancellationToken cancellationToken) =>
        await FactsAsync(
            await db.Remediations.AsNoTracking().Where(r => r.ProjectId == projectId && r.IsBackfill && r.Status != "closed").ToListAsync(cancellationToken),
            cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> ProjectNamesAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var id in projectIds.Distinct())
        {
            if (await profiles.FindProjectAsync(id, cancellationToken) is { } project)
            {
                names[id] = project.Name;
            }
        }

        return names;
    }

    public async Task<IReadOnlyList<FindingGrowth>> FindingsJoinedAsync(DateTimeOffset after, DateTimeOffset until, CancellationToken cancellationToken) =>
        (await db.Events.AsNoTracking()
            .Where(e => e.Kind == EventKinds.FindingJoined && e.RemediationId != null && e.At > after && e.At <= until)
            .GroupBy(e => e.RemediationId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken))
        .Select(g => new FindingGrowth(g.Key, g.Count)).ToList();

    private async Task<IReadOnlyList<ActionFacts>> FactsAsync(IReadOnlyList<Remediation> actions, CancellationToken cancellationToken)
    {
        if (actions.Count == 0)
        {
            return [];
        }

        var ids = actions.Select(a => a.Id).ToArray();
        var findings = (await db.Findings.AsNoTracking()
                .Where(f => f.RemediationId != null && ids.Contains(f.RemediationId.Value))
                .Select(f => new { RemediationId = f.RemediationId!.Value, f.EntryId, f.Level, f.Status, f.Exploitation })
                .ToListAsync(cancellationToken))
            .ToLookup(f => f.RemediationId);
        var entryIds = findings.SelectMany(g => g).Select(f => f.EntryId).Distinct().ToArray();
        var keys = await db.Alerts.AsNoTracking().Where(a => entryIds.Contains(a.EntryId)).ToDictionaryAsync(a => a.EntryId, a => a.EntryKey, cancellationToken);
        var titles = (await entries.FindManyAsync(entryIds, cancellationToken)).ToDictionary(e => e.Id, e => e.Title);
        var components = (await profiles.FindComponentsAsync([.. actions.Select(a => a.ComponentId).Distinct()], cancellationToken)).ToDictionary(c => c.Id);
        var projectNames = await ProjectNamesAsync([.. actions.Where(a => !components.ContainsKey(a.ComponentId)).Select(a => a.ProjectId)], cancellationToken);
        return [.. actions.Select(a =>
        {
            var component = components.GetValueOrDefault(a.ComponentId);
            var top = findings[a.Id]
                .OrderByDescending(f => f.Status == "open").ThenByDescending(f => f.Level)
                .Select(f => new ActionEntryFacts(keys.GetValueOrDefault(f.EntryId, string.Empty), titles.GetValueOrDefault(f.EntryId), f.Level, f.Exploitation))
                .DistinctBy(e => e.Key).Take(3).ToList();
            return new ActionFacts
            {
                Id = a.Id,
                Number = a.Number,
                Level = a.Level,
                Status = a.Status,
                ProjectId = a.ProjectId,
                ProjectName = component?.ProjectName ?? projectNames.GetValueOrDefault(a.ProjectId, string.Empty),
                ComponentName = component?.Name ?? string.Empty,
                ComponentVersion = component?.Version,
                OpenFindings = a.OpenFindings,
                Exploitation = a.Exploitation,
                FixAvailable = a.FixAvailable,
                TargetVersion = a.TargetVersion,
                IsBackfill = a.IsBackfill,
                CloseReason = a.CloseReason,
                Entries = top,
            };
        })];
    }
}
