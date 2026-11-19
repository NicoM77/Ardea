using Ardea.Assessment.Persistence;
using Ardea.Assessment.Rules;
using Ardea.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Assessing;

/// <summary>
/// Keeps the actions in step with their findings (master plan 7.4, 4.9, 8.2): one action per component
/// with open findings, its level the maximum over them, its deadline from the level, and an event for
/// every change a person must hear about. Called once at the end of every unit of work that changed
/// findings, with the components it touched.
/// </summary>
internal sealed class Remediations(AssessDbContext db, Numbers numbers, History history, RuleSet rules, IVersionSchemes schemes, TimeProvider time)
{
    private static readonly Dictionary<string, int> ExploitationRank = new(StringComparer.Ordinal) { ["none"] = 0, ["poc"] = 1, ["active"] = 2 };

    /// <param name="componentIds">The components whose findings changed.</param>
    /// <param name="actor">The person whose change this is; the caller records its own event for a closure then.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task SyncAsync(IEnumerable<Guid> componentIds, Guid? actor, CancellationToken cancellationToken)
    {
        foreach (var component in componentIds.Distinct().Order())
        {
            // Two jobs touching one component take turns; the order of the ids keeps them from deadlocking.
            await LockAsync(component, cancellationToken);
            await SyncComponentAsync(component, actor, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Takes the component's lock until the unit of work ends. Jobs and people's changes take it before they
    /// read findings or actions of the component, so they take turns and always lock in the same order.
    /// The lock is re-entrant: <see cref="SyncAsync"/> may take it again in the same unit of work.
    /// </summary>
    public async Task LockAsync(Guid componentId, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"assess.component:" + componentId}, 0))", cancellationToken);

    private async Task SyncComponentAsync(Guid componentId, Guid? actor, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var findings = await db.Findings.Where(f => f.ComponentId == componentId).ToListAsync(cancellationToken);
        var active = findings.Where(f => f.Status is "open" or "in_review").ToList();
        var current = await db.Remediations.SingleOrDefaultAsync(r => r.ComponentId == componentId && r.Status != "closed", cancellationToken);

        if (active.Count == 0)
        {
            if (current is not null)
            {
                Close(current, findings.Where(f => f.RemediationId == current.Id), actor, now);
            }

            return;
        }

        var created = current is null;
        if (current is null)
        {
            current = new Remediation
            {
                Id = Guid.CreateVersion7(),
                ProjectId = active[0].ProjectId,
                ComponentId = componentId,
                Number = await numbers.NextAsync(cancellationToken),
                Status = "open",
                OpenedAt = now,
                Level = active.Max(f => f.Level),
                IsBackfill = active.TrueForAll(f => f.IsBackfill),
                ChangedAt = now,
            };
            db.Remediations.Add(current);
        }

        var (level, exploitation, fix) = (current.Level, current.Exploitation, current.FixAvailable);
        foreach (var finding in active.Where(f => f.RemediationId != current.Id))
        {
            // A finding joins the action and takes its status, open or in review (master plan 7.4).
            finding.RemediationId = current.Id;
            finding.Status = current.Status;
            finding.ChangedAt = now;
            if (!finding.IsBackfill)
            {
                history.Add(EventKinds.FindingJoined, new { finding.Level, action = current.Number }, current.Id, finding.Id);
            }
        }

        current.Level = active.Max(f => f.Level);
        current.OpenFindings = active.Count;
        current.Exploitation = active.Select(f => f.Exploitation).MaxBy(e => ExploitationRank.GetValueOrDefault(e)) ?? "none";
        current.FixAvailable = active.Exists(f => f.FixAvailable);
        current.TargetVersion = Highest(active.Select(f => f.FixedVersion).OfType<string>());
        if (created)
        {
            if (!current.IsBackfill)
            {
                history.Add(EventKinds.ActionRaised, new { current.Level, findings = active.Count, current.Exploitation }, current.Id);
            }
        }
        else if (current.Level > level)
        {
            current.LevelRaisedAt = now;
            history.Add(EventKinds.ActionLevelRaised, new { from = level, to = current.Level }, current.Id);
        }
        else if (current.Level < level)
        {
            history.Add(EventKinds.ActionLevelLowered, new { from = level, to = current.Level }, current.Id);
        }
        else if (ExploitationRank.GetValueOrDefault(current.Exploitation) > ExploitationRank.GetValueOrDefault(exploitation))
        {
            history.Add(EventKinds.ActionExploitationEscalated, new { from = exploitation, to = current.Exploitation }, current.Id);
        }

        if (!created && current.FixAvailable && !fix)
        {
            history.Add(EventKinds.ActionFixAvailable, new { current.TargetVersion }, current.Id);
        }

        if (!current.DueAtManual)
        {
            // Start plus the deadline of the current level; a lower level keeps the start and gets the longer deadline (master plan 4.9).
            var start = current.LevelRaisedAt ?? current.OpenedAt;
            current.DueAt = rules.DeadlineOf(current.Level) is { } deadline ? start + deadline : null;
        }

        current.ChangedAt = now;
    }

    private void Close(Remediation remediation, IEnumerable<Finding> findings, Guid? actor, DateTimeOffset now)
    {
        // The most frequent outcome of its findings (master plan 4.9); a reason of the system says more than its status.
        var reason = findings.GroupBy(f => f.StatusReason ?? f.Status)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key).FirstOrDefault() ?? "remediated";
        remediation.Status = "closed";
        remediation.ClosedAt = now;
        remediation.CloseReason = reason;
        remediation.OpenFindings = 0;
        remediation.ChangedAt = now;
        if (actor is null)
        {
            history.Add(EventKinds.ActionWithdrawn, new { reason }, remediation.Id);
        }
    }

    /// <summary>The highest version, compared in the tolerant numeric scheme; text order where it cannot read them.</summary>
    private string? Highest(IEnumerable<string> versions)
    {
        var scheme = schemes.Find(VersionSchemeNames.Numeric);
        string? best = null;
        ParsedVersion? bestParsed = null;
        foreach (var version in versions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            ParsedVersion? parsed = scheme is not null && scheme.TryParse(version, out var p) ? p : null;
            if (best is null
                || (parsed is not null && (bestParsed is null || parsed > bestParsed))
                || (parsed is null && bestParsed is null && string.CompareOrdinal(version, best) > 0))
            {
                (best, bestParsed) = (version, parsed);
            }
        }

        return best;
    }
}
