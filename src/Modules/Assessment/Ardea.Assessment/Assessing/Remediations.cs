using Ardea.Assessment.Persistence;
using Ardea.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Assessing;

internal sealed class Remediations(AssessDbContext db, Numbers numbers, History history, IVersionSchemes schemes, TimeProvider time)
{
    private static readonly Dictionary<string, int> ExploitationRank = new(StringComparer.Ordinal) { ["none"] = 0, ["poc"] = 1, ["active"] = 2 };

    public async Task SyncAsync(IEnumerable<Guid> componentIds, Guid? actor, CancellationToken cancellationToken)
    {
        foreach (var component in componentIds.Distinct().Order())
        {
            await LockAsync(component, cancellationToken);
            await SyncComponentAsync(component, actor, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task LockAsync(Guid componentId, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"assess.component:" + componentId}, 0))", cancellationToken);

    private async Task SyncComponentAsync(Guid componentId, Guid? actor, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var findings = await db.Findings.Where(f => f.ComponentId == componentId).ToListAsync(cancellationToken);
        var active = findings.Where(f => f.Status == "open").ToList();
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
            finding.RemediationId = current.Id;
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

        current.ChangedAt = now;
    }

    private void Close(Remediation remediation, IEnumerable<Finding> findings, Guid? actor, DateTimeOffset now)
    {
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
