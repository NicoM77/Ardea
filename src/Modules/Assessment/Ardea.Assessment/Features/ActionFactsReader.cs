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
        return [.. rows.Where(e => EventKinds.IsNotifiable(e.Kind))
            .Select(e => new ActionEventFacts(e.Id, e.Kind, e.At, e.RemediationId, e.ProjectId, e.Actor, e.Data.RootElement.Clone()))];
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
                .OrderByDescending(f => f.Status is "open" or "in_review").ThenByDescending(f => f.Level)
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
                AssignedTo = a.AssignedTo,
                IsBackfill = a.IsBackfill,
                CloseReason = a.CloseReason,
                Entries = top,
            };
        })];
    }
}
