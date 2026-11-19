using System.Text.Json;
using Ardea.Assessment.Assessing;
using Ardea.Assessment.Persistence;
using Ardea.Assessment.Rules;
using Ardea.Platform.Audit;
using Ardea.Platform.Jobs;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Features;

/// <summary>
/// What people do with actions and findings (master plan 7.4, 4.9, 10.2, 17.5): an outcome for all findings of an
/// action, the outcome of a single finding, taking a closing back, notes. An action is open or done; nobody is
/// assigned and there is no deadline (ADR 046). Every change checks the status the caller saw, is audited and
/// written to the history.
/// </summary>
internal sealed class ActionCommands(
    AssessDbContext db, Remediations remediations, History history, RuleSet rules, IAuditLog audit, IJobQueue jobs, ITenantContext tenant, TimeProvider time)
{
    public static readonly string[] Outcomes = ["remediated", "risk_accepted", "not_affected"];

    public async Task<CommandResult<Remediation>> SetActionStatusAsync(int number, SetActionStatusRequest request, Guid user, CancellationToken cancellationToken)
    {
        var status = request.Status?.Trim().ToLowerInvariant();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (status != "open" && !Outcomes.Contains(status))
        {
            errors["status"] = ["Choose remediated, risk_accepted or not_affected, or open to take a closing back."];
        }

        if (request.ExpectedStatus is not ("open" or "closed"))
        {
            errors["expectedStatus"] = ["Send the status you saw: open or closed."];
        }

        CheckNote(request.Note, errors);
        if (errors.Count > 0)
        {
            return Refusal.Validation("invalid_status_change", "The status change is not valid.", errors);
        }

        var action = await LockedActionAsync(number, cancellationToken);
        if (action is null)
        {
            return NotFound();
        }

        if (action.Status != request.ExpectedStatus)
        {
            return Refusal.Conflict("status_conflict", $"The action is {action.Status} by now; reload it and decide again.");
        }

        var now = time.GetUtcNow();
        var from = action.Status;
        var findings = await db.Findings.Where(f => f.RemediationId == action.Id).ToListAsync(cancellationToken);
        if (status == "open")
        {
            if (action.Status != "closed")
            {
                return Refusal.Conflict("action_open", "The action is open already.");
            }

            // Taking a closing back: the outcomes people set return to the action (master plan 3.10 «Rücknahme»).
            if (await db.Remediations.AnyAsync(r => r.ComponentId == action.ComponentId && r.Status != "closed", cancellationToken))
            {
                return Refusal.Conflict("action_superseded", "A newer action for this component is open; work there.");
            }

            var back = findings.Where(f => f.StatusChangedBy is not null && Outcomes.Contains(f.Status)).ToList();
            if (back.Count == 0)
            {
                return Refusal.Conflict("nothing_to_reopen", "No finding of this action was closed by a person.");
            }

            foreach (var finding in back)
            {
                Reopen(finding, user, now, request.Note);
            }

            action.Status = status;
            action.ClosedAt = null;
            action.CloseReason = null;
        }
        else
        {
            if (action.Status == "closed")
            {
                return Refusal.Conflict("action_closed", "The action is closed; take the closing back first.");
            }

            foreach (var finding in findings.Where(f => f.Status == "open"))
            {
                Conclude(finding, status!, user, request.Note, now);
            }
        }

        action.ChangedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        history.Add(EventKinds.ActionStatusChanged, new { from, to = status, note = Note(request.Note) }, action.Id, actor: user);
        await remediations.SyncAsync([action.ComponentId], user, cancellationToken);
        await UpdateAlertsAsync(findings.Select(f => f.AlertId), cancellationToken);
        await audit.RecordAsync(new AuditEntry("action.status_changed", "action", number.ToString(System.Globalization.CultureInfo.InvariantCulture), new { from, to = status }), cancellationToken);
        return action;
    }

    public async Task<CommandResult<Finding>> SetFindingStatusAsync(Guid findingId, SetFindingStatusRequest request, Guid user, CancellationToken cancellationToken)
    {
        var status = request.Status?.Trim().ToLowerInvariant();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (status != "open" && !Outcomes.Contains(status))
        {
            errors["status"] = ["Choose remediated, risk_accepted or not_affected, or open to take a closing back."];
        }

        if (string.IsNullOrWhiteSpace(request.ExpectedStatus))
        {
            errors["expectedStatus"] = ["Send the status you saw."];
        }

        CheckNote(request.Note, errors);
        if (errors.Count > 0)
        {
            return Refusal.Validation("invalid_status_change", "The status change is not valid.", errors);
        }

        var finding = await db.Findings.SingleOrDefaultAsync(f => f.Id == findingId, cancellationToken);
        if (finding is null)
        {
            return Refusal.NotFound("finding_not_found", "No such finding.");
        }

        // The status the caller saw is compared under the component's lock, so two changes cannot both pass.
        await remediations.LockAsync(finding.ComponentId, cancellationToken);
        await db.Entry(finding).ReloadAsync(cancellationToken);

        if (finding.Status != request.ExpectedStatus)
        {
            return Refusal.Conflict("status_conflict", $"The finding is {finding.Status} by now; reload it and decide again.");
        }

        var now = time.GetUtcNow();
        var from = finding.Status;
        if (status == "open")
        {
            if (finding.Status == "open")
            {
                return Refusal.Conflict("finding_open", "The finding is open already.");
            }

            // It joins the open action of its component or opens a new one; the rules compute its level again.
            Reopen(finding, user, now);
            finding.RemediationId = await db.Remediations.Where(r => r.ComponentId == finding.ComponentId && r.Status != "closed")
                .Select(r => (Guid?)r.Id).SingleOrDefaultAsync(cancellationToken);
            await jobs.EnqueueAsync(new AssessAlert(finding.EntryId), AssessAlert.Options(tenant.RequiredOrganizationId, finding.EntryId), cancellationToken);
        }
        else
        {
            Conclude(finding, status!, user, request.Note, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        history.Add(EventKinds.FindingStatusChanged, new { from, to = status, note = Note(request.Note) }, finding.RemediationId, finding.Id, actor: user);
        await remediations.SyncAsync([finding.ComponentId], user, cancellationToken);
        await UpdateAlertsAsync([finding.AlertId], cancellationToken);
        await audit.RecordAsync(new AuditEntry("finding.status_changed", "finding", finding.Id.ToString(), new { from, to = status }), cancellationToken);
        return finding;
    }

    public async Task<CommandResult<AssessEvent>> AddNoteAsync(int number, AddNoteRequest request, Guid user, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            errors["text"] = ["Write a note."];
        }

        CheckNote(request.Text, errors, "text");
        if (errors.Count > 0)
        {
            return Refusal.Validation("invalid_note", "The note is not valid.", errors);
        }

        var action = await db.Remediations.SingleOrDefaultAsync(r => r.Number == number, cancellationToken);
        if (action is null)
        {
            return NotFound();
        }

        var row = history.Add(EventKinds.ActionNoteAdded, new { text = request.Text!.Trim() }, action.Id, actor: user);
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    /// <summary>
    /// The action, read again after taking its component's lock: a check against the status the caller saw
    /// then holds until the unit of work commits, and a concurrent change waits instead of being overwritten.
    /// </summary>
    private async Task<Remediation?> LockedActionAsync(int number, CancellationToken cancellationToken)
    {
        var action = await db.Remediations.SingleOrDefaultAsync(r => r.Number == number, cancellationToken);
        if (action is not null)
        {
            await remediations.LockAsync(action.ComponentId, cancellationToken);
            await db.Entry(action).ReloadAsync(cancellationToken);
        }

        return action;
    }

    private static Refusal NotFound() => Refusal.NotFound("action_not_found", "No such action.");

    private static void CheckNote(string? note, Dictionary<string, string[]> errors, string field = "note")
    {
        if (note?.Trim().Length > 2000)
        {
            errors[field] = ["At most 2000 characters."];
        }
    }

    private static string? Note(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static void SetPersonStatus(Finding finding, string status, Guid user, string? note, DateTimeOffset now)
    {
        finding.Status = status;
        finding.StatusReason = null;
        finding.StatusChangedBy = user;
        finding.StatusChangedAt = now;
        finding.StatusNote = Note(note) ?? finding.StatusNote;
        finding.ChangedAt = now;
    }

    /// <summary>A person's outcome; «not affected» is an exclusion and gives level 1 (master plan 7.6 step 1).</summary>
    internal void Conclude(Finding finding, string outcome, Guid user, string? note, DateTimeOffset now)
    {
        if (finding.Status == "open")
        {
            finding.ClosedLevel = finding.Level;
            finding.ClosedVersionVerdict = finding.VersionVerdict;
        }

        SetPersonStatus(finding, outcome, user, note, now);
        if (outcome == "not_affected")
        {
            finding.Level = 1;
            finding.Factors = JsonSerializer.SerializeToDocument(new RuleResult { RulesetVersion = rules.Version, Level = 1, Exclusion = "human" }, AlertAssessor.Json);
        }
    }

    private static void Reopen(Finding finding, Guid user, DateTimeOffset now, string? note = null)
    {
        // Until the rules compute it again, it keeps the level it had when it closed, at least level 2.
        finding.Level = Math.Max(finding.ClosedLevel ?? finding.Level, 2);
        finding.ClosedLevel = null;
        finding.ClosedVersionVerdict = null;

        // A person's «not affected» goes with the closing it came with (task 4.7); the rules give the verdict again.
        if (finding is { VerdictSource: "human", Verdict: "not_affected" })
        {
            finding.Verdict = finding.VersionVerdict switch
            {
                "in_range" => "affected",
                "unknown" => "uncertain",
                _ => "not_affected",
            };
            finding.VerdictSource = "rules";
        }

        SetPersonStatus(finding, "open", user, note, now);
    }

    private async Task UpdateAlertsAsync(IEnumerable<Guid> alertIds, CancellationToken cancellationToken)
    {
        var ids = alertIds.Distinct().ToArray();
        foreach (var alert in await db.Alerts.Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken))
        {
            alert.Level = await db.Findings.Where(f => f.AlertId == alert.Id).MaxAsync(f => (int?)f.Level, cancellationToken) ?? 1;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
