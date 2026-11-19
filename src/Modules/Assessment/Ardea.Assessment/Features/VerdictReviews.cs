using Ardea.Assessment.Assessing;
using Ardea.Assessment.Persistence;
using Ardea.Intel.Contracts;
using Ardea.Platform.Audit;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Features;

/// <summary>
/// «Einschätzung korrigieren» (master plan 7.7, 7.9; task 4.7): a person confirms the model's estimate of a finding,
/// which makes it a person's verdict, or corrects the verdict or the level with a reason. Either is stored as feedback
/// and in the history, and only this finding is assessed again. A person's verdict is no longer the model's: the floor
/// «at most one level below the result without the model» (master plan 7.6) no longer binds it.
/// </summary>
internal sealed class VerdictReviews(
    AssessDbContext db,
    AlertAssessor assessor,
    Remediations remediations,
    ActionCommands commands,
    History history,
    IEntryQueries entries,
    IAuditLog audit,
    TimeProvider time)
{
    public const string Confirm = "confirm";
    public const string Correct = "correct";

    public static readonly string[] Verdicts = ["affected", "likely", "uncertain", "unlikely", "not_affected"];

    private static readonly string[] Sources = ["rules", "model", "human"];

    public async Task<CommandResult<Finding>> ReviewAsync(Guid findingId, ReviewVerdictRequest request, Guid user, CancellationToken cancellationToken)
    {
        var decision = request.Decision?.Trim().ToLowerInvariant();
        var verdict = request.Verdict?.Trim().ToLowerInvariant();
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (decision is not (Confirm or Correct))
        {
            errors["decision"] = ["Choose confirm or correct."];
        }

        if (!Verdicts.Contains(request.ExpectedVerdict) || !Sources.Contains(request.ExpectedSource))
        {
            errors["expectedVerdict"] = ["Send the verdict and its source as you saw them."];
        }

        if (reason?.Length > 2000)
        {
            errors["reason"] = ["At most 2000 characters."];
        }

        if (decision == Confirm && (verdict is not null || request.Level is not null))
        {
            errors["decision"] = ["A confirmation takes the estimate as it is; correct it to change verdict or level."];
        }

        if (decision == Correct)
        {
            if (verdict is not null && !Verdicts.Contains(verdict))
            {
                errors["verdict"] = ["Choose affected, likely, uncertain, unlikely or not_affected."];
            }

            if (request.Level is { } level && level is < 1 or > 5)
            {
                errors["level"] = ["A level from 1 to 5."];
            }

            if (verdict is null && request.Level is null)
            {
                errors["verdict"] = ["Name the verdict or the level you expect."];
            }

            if (reason is null)
            {
                errors["reason"] = ["Say why the assessment is wrong."];
            }
        }

        if (errors.Count > 0)
        {
            return Refusal.Validation("invalid_review", "The review is not valid.", errors);
        }

        var finding = await db.Findings.SingleOrDefaultAsync(f => f.Id == findingId, cancellationToken);
        if (finding is null)
        {
            return Refusal.NotFound("finding_not_found", "No such finding.");
        }

        // The verdict the caller saw is compared under the alert's and the component's locks, taken in the order the
        // jobs take them, so a reassessment cannot change it between the check and the write.
        await assessor.LockAsync(finding.EntryId, cancellationToken);
        await remediations.LockAsync(finding.ComponentId, cancellationToken);
        await db.Entry(finding).ReloadAsync(cancellationToken);

        if (finding.Verdict != request.ExpectedVerdict || finding.VerdictSource != request.ExpectedSource)
        {
            return Refusal.Conflict("verdict_conflict", $"The verdict is {finding.Verdict} ({finding.VerdictSource}) by now; reload it and decide again.");
        }

        if (finding.Status is not ("open" or "in_review"))
        {
            return Refusal.Conflict("finding_closed", "The finding is closed; take the closing back to review it.");
        }

        if (decision == Confirm && finding.VerdictSource != "model")
        {
            return Refusal.Conflict("nothing_to_confirm", "Only the model's estimate can be confirmed; correct the verdict instead.");
        }

        var newVerdict = decision == Confirm ? finding.Verdict : verdict;
        var assessmentId = finding.VerdictSource == "model"
            ? await db.Alerts.Where(a => a.Id == finding.AlertId).Select(a => a.CurrentAssessmentId).SingleAsync(cancellationToken)
            : null;
        var now = time.GetUtcNow();
        db.Feedback.Add(new AssessmentFeedback
        {
            Id = Guid.CreateVersion7(),
            FindingId = finding.Id,
            AlertId = finding.AlertId,
            AssessmentId = assessmentId,
            Kind = decision == Confirm ? AssessmentFeedback.Confirmed : AssessmentFeedback.Corrected,
            VerdictSource = finding.VerdictSource,
            VerdictBefore = finding.Verdict,
            Verdict = newVerdict,
            LevelBefore = finding.Level,
            ExpectedLevel = request.Level,
            Reason = reason,
            Actor = user,
            CreatedAt = now,
        });
        history.Add(
            decision == Confirm ? EventKinds.FindingVerdictConfirmed : EventKinds.FindingVerdictCorrected,
            new
            {
                from = finding.Verdict,
                source = finding.VerdictSource,
                to = newVerdict,
                level = finding.Level,
                expectedLevel = request.Level,
                reason,
                closed = newVerdict == "not_affected",
            },
            finding.RemediationId,
            finding.Id,
            actor: user);

        if (newVerdict is not null && (newVerdict != finding.Verdict || finding.VerdictSource != "human"))
        {
            finding.Verdict = newVerdict;
            finding.VerdictSource = "human";
            finding.ChangedAt = now;

            // A person's «not affected» is an exclusion (master plan 7.5, 7.6 step 1): the proposal becomes the outcome.
            if (newVerdict == "not_affected")
            {
                commands.Conclude(finding, "not_affected", user, reason, now);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Only this finding, with the person's verdict as an input of the rules (master plan 7.7).
        var touched = new HashSet<Guid> { finding.ComponentId };
        if (await entries.FindAsync(finding.EntryId, cancellationToken) is { } entry)
        {
            touched.UnionWith(await assessor.AssessAsync(entry, AssessmentScope.ForComponent(finding.ComponentId), cancellationToken));
        }

        await remediations.SyncAsync(touched, user, cancellationToken);
        await audit.RecordAsync(
            new AuditEntry(
                "finding.verdict_reviewed",
                "finding",
                finding.Id.ToString(),
                new { decision, from = request.ExpectedVerdict, source = request.ExpectedSource, to = newVerdict, expectedLevel = request.Level }),
            cancellationToken);
        return finding;
    }
}
