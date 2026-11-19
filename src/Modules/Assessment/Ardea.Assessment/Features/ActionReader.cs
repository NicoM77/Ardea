using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Intel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Features;

/// <summary>Builds the views of actions, findings and alerts with names from Assets and titles from Intel.</summary>
internal sealed class ActionReader(AssessDbContext db, IProfileQueries profiles, IEntryQueries entries, TimeProvider time)
{
    public async Task<IReadOnlyList<ActionSummary>> SummariesAsync(IReadOnlyList<Remediation> actions, CancellationToken cancellationToken)
    {
        if (actions.Count == 0)
        {
            return [];
        }

        var components = await ComponentsAsync(actions.Select(a => a.ComponentId), cancellationToken);
        var ids = actions.Select(a => a.Id).ToArray();
        var keys = (await (
                from finding in db.Findings.AsNoTracking()
                join alert in db.Alerts.AsNoTracking() on finding.AlertId equals alert.Id
                where finding.RemediationId != null && ids.Contains(finding.RemediationId.Value)
                select new { RemediationId = finding.RemediationId!.Value, finding.Level, finding.Status, alert.EntryKey })
            .ToListAsync(cancellationToken))
            .ToLookup(k => k.RemediationId);
        var now = time.GetUtcNow();
        return [.. actions.Select(a =>
        {
            var (project, component) = Refs(components, a.ComponentId, a.ProjectId);
            var entryKeys = keys[a.Id]
                .OrderByDescending(k => k.Status is "open" or "in_review").ThenByDescending(k => k.Level).ThenBy(k => k.EntryKey, StringComparer.Ordinal)
                .Select(k => k.EntryKey).Distinct().Take(3).ToList();
            return new ActionSummary(
                a.Number, a.Level, Levels.Name(a.Level), a.Status, project, component, a.TargetVersion, a.OpenFindings, a.Exploitation, a.FixAvailable,
                a.AssignedTo, a.DueAt, a.Status != "closed" && a.DueAt < now, a.OpenedAt, a.LevelRaisedAt, a.ClosedAt, a.CloseReason, a.IsBackfill, entryKeys);
        })];
    }

    public async Task<IReadOnlyList<FindingView>> FindingsAsync(IReadOnlyList<Finding> findings, CancellationToken cancellationToken)
    {
        if (findings.Count == 0)
        {
            return [];
        }

        var components = await ComponentsAsync(findings.Select(f => f.ComponentId), cancellationToken);
        var alertIds = findings.Select(f => f.AlertId).Distinct().ToArray();
        var alerts = await db.Alerts.AsNoTracking().Where(a => alertIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);
        var actionIds = findings.Select(f => f.RemediationId).OfType<Guid>().Distinct().ToArray();
        var actions = await db.Remediations.AsNoTracking().Where(r => actionIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Number, cancellationToken);
        var titles = (await entries.FindManyAsync([.. findings.Select(f => f.EntryId).Distinct()], cancellationToken)).ToDictionary(e => e.Id, e => e.Title);
        return [.. findings.Select(f =>
        {
            var (project, component) = Refs(components, f.ComponentId, f.ProjectId);
            var alert = alerts[f.AlertId];
            return new FindingView(
                f.Id, alert.Number, f.RemediationId is { } r ? actions.GetValueOrDefault(r) : null,
                new ActionEntryRef(f.EntryId, alert.EntryKey, titles.GetValueOrDefault(f.EntryId)), project, component,
                f.Level, Levels.Name(f.Level), f.Status, f.StatusReason, f.StatusNote, f.StatusChangedBy, f.StatusChangedAt, f.MatchType,
                f.VersionVerdict, f.VersionReason, f.FixedVersion, f.Verdict, f.VerdictSource, f.Exploitation, f.FixAvailable, f.IsBackfill,
                f.Factors.RootElement.Clone(), f.Evidence.RootElement.Clone());
        })];
    }

    public async Task<IReadOnlyList<AlertSummary>> AlertSummariesAsync(IReadOnlyList<Alert> alerts, CancellationToken cancellationToken)
    {
        if (alerts.Count == 0)
        {
            return [];
        }

        var ids = alerts.Select(a => a.Id).ToArray();
        var counts = (await db.Findings.AsNoTracking().Where(f => ids.Contains(f.AlertId))
                .Select(f => new { f.AlertId, f.Status, f.RemediationId }).ToListAsync(cancellationToken))
            .ToLookup(f => f.AlertId);
        var titles = (await entries.FindManyAsync([.. alerts.Select(a => a.EntryId)], cancellationToken)).ToDictionary(e => e.Id, e => e.Title);
        return [.. alerts.Select(a => new AlertSummary(
            a.Number ?? 0, a.Level, Levels.Name(a.Level), new ActionEntryRef(a.EntryId, a.EntryKey, titles.GetValueOrDefault(a.EntryId)),
            counts[a.Id].Where(f => f.Status is "open" or "in_review").Select(f => f.RemediationId).Distinct().Count(),
            counts[a.Id].Count(), a.FirstRaisedAt, a.ChangedAt))];
    }

    /// <summary>The three answers for an action's or an alert's findings (master plan 7.5; task 3.6).</summary>
    public async Task<AnswersView?> AnswersAsync(IReadOnlyList<Finding> findings, string? targetVersion, CancellationToken cancellationToken) =>
        findings.Count == 0 ? null : (await AnswersAsync([(findings, targetVersion)], cancellationToken))[0];

    /// <summary>The answers for several actions at once: one query per kind of data, not per action.</summary>
    public async Task<IReadOnlyList<AnswersView>> AnswersAsync(
        IReadOnlyList<(IReadOnlyList<Finding> Findings, string? TargetVersion)> sets, CancellationToken cancellationToken)
    {
        var all = sets.SelectMany(s => s.Findings).ToList();
        var components = await ComponentsAsync(all.Select(f => f.ComponentId), cancellationToken);
        var texts = (await entries.TextsAsync([.. all.Select(f => f.EntryId).Distinct()], cancellationToken)).ToDictionary(t => t.Id);
        var assessments = await CurrentAssessmentsAsync(all.Select(f => f.AlertId), cancellationToken);
        return [.. sets.Select(s => AnswerTemplates.For(s.Findings, texts, f => Refs(components, f.ComponentId, f.ProjectId), s.TargetVersion, assessments))];
    }

    /// <summary>
    /// The current assessment per alert, where it was made for the revision of the entry the findings come from; an
    /// older one no longer speaks for the alert, and the templates answer until the model looks again.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, AlertAssessment>> CurrentAssessmentsAsync(IEnumerable<Guid> alertIds, CancellationToken cancellationToken)
    {
        var ids = alertIds.Distinct().ToArray();
        var current = await (
                from alert in db.Alerts.AsNoTracking()
                join assessment in db.Assessments.AsNoTracking() on alert.CurrentAssessmentId equals (Guid?)assessment.Id
                where ids.Contains(alert.Id) && assessment.EntryRevision == alert.AssessedEntryRevision
                select assessment)
            .ToListAsync(cancellationToken);
        return current.ToDictionary(a => a.AlertId);
    }

    public async Task<IReadOnlyList<ActionEventView>> EventsAsync(Guid remediationId, CancellationToken cancellationToken) =>
        (await db.Events.AsNoTracking().Where(e => e.RemediationId == remediationId).OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync(cancellationToken))
        .Select(View).ToList();

    public static ActionEventView View(AssessEvent e) => new(e.Id, e.Kind, e.At, e.Actor, e.FindingId, e.Data.RootElement.Clone());

    private async Task<Dictionary<Guid, ProfileComponent>> ComponentsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        (await profiles.FindComponentsAsync([.. ids.Distinct()], cancellationToken)).ToDictionary(c => c.Id);

    private static (ActionProjectRef Project, ActionComponentRef Component) Refs(Dictionary<Guid, ProfileComponent> components, Guid componentId, Guid projectId) =>
        components.TryGetValue(componentId, out var c)
            ? (new ActionProjectRef(projectId, c.ProjectName), new ActionComponentRef(componentId, c.Name, c.Version, Removed: false))
            : (new ActionProjectRef(projectId, string.Empty), new ActionComponentRef(componentId, string.Empty, null, Removed: true));
}
