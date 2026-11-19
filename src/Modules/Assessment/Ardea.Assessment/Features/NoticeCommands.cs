using Ardea.Assessment.Assessing;
using Ardea.Assessment.Matching;
using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Platform.Audit;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Features;

/// <summary>
/// What people answer to a notice (master plan 17.3, E21, E22; ADR 047). Every answer checks the state the caller saw under
/// the alert's lock, which the assessment of the entry takes too, is audited and written to the notice's history.
/// <list type="bullet">
/// <item>«Doesn't affect us» closes the notice. It comes back once, when the entry becomes actively exploited (KEV included).</item>
/// <item>«Affects us» opens a normal reminder: on the stack entry the notice names, or in a project; a version is recorded on
/// that project's component of the product, which then takes the precise path (E21). The entry joins the open reminder of
/// that component when there is one.</item>
/// <item>Taking an answer back reopens the notice; the findings «affects us» opened return to the notice, which closes their
/// reminder when nothing else keeps it open. A version recorded stays: it is what the person said about the profile.</item>
/// </list>
/// </summary>
internal sealed class NoticeCommands(
    AssessDbContext db,
    AlertAssessor assessor,
    Remediations remediations,
    History history,
    IEntryQueries entries,
    IProfileQueries profiles,
    IProfilePlacement placement,
    IAuditLog audit,
    TimeProvider time)
{
    private const int MaxVersionLength = 100;

    public async Task<CommandResult<Notice>> AnswerAsync(Guid noticeId, AnswerNoticeRequest request, Guid user, CancellationToken cancellationToken)
    {
        var answer = request.Answer?.Trim().ToLowerInvariant();
        var version = string.IsNullOrWhiteSpace(request.Version) ? null : request.Version.Trim();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (answer is not (Notice.AffectsUs or Notice.NotAffected))
        {
            errors["answer"] = ["Answer affects_us or not_affected."];
        }

        if (request.ExpectedState != Notice.Open)
        {
            errors["expectedState"] = ["Send the state you saw: open."];
        }

        if (answer == Notice.NotAffected && (request.ProjectId is not null || version is not null))
        {
            errors["answer"] = ["Only «affects us» takes a project and a version."];
        }

        if (version?.Length > MaxVersionLength)
        {
            errors["version"] = [$"Use at most {MaxVersionLength} characters."];
        }

        if (errors.Count > 0)
        {
            return Refusal.Validation("invalid_answer", "The answer is not valid.", errors);
        }

        var locked = await LockedAsync(noticeId, request.ExpectedState!, cancellationToken);
        if (locked.Refusal is { } refused)
        {
            return refused;
        }

        var (notice, entry) = locked.Value;
        if (notice.State != Notice.Open)
        {
            return Refusal.Conflict("notice_answered", "Only an open notice takes an answer; take the answer back first.");
        }

        var now = time.GetUtcNow();
        if (answer == Notice.NotAffected)
        {
            Decide(notice, Notice.NotAffected, user, entry, now);
            await db.SaveChangesAsync(cancellationToken);
            history.Add(EventKinds.NoticeAnswered, new { answer, entry = entry.Key }, actor: user, notice: notice.Id);
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(new AuditEntry("notice.answered", "notice", notice.Id.ToString(), new { answer, entry = entry.Key }), cancellationToken);
            return notice;
        }

        var placed = await PlaceAsync(notice, request.ProjectId, version, cancellationToken);
        if (placed.Refusal is { } notPlaced)
        {
            return notPlaced;
        }

        // The answer comes first: the assessment below keeps an answered notice's state.
        var targets = placed.Value!;
        Decide(notice, Notice.AffectsUs, user, entry, now);
        await db.SaveChangesAsync(cancellationToken);

        // The entry on every target: the precise path where the version is exact, else the finding a person opens (E21).
        var scope = AssessmentScope.ForComponents(targets);
        var touched = new HashSet<Guid>(await assessor.AssessAsync(entry, scope, cancellationToken));
        var coarse = (await profiles.FindComponentsAsync(targets, cancellationToken)).Where(NoticeRules.IsCoarse).Select(c => c.Id).Order().ToArray();
        foreach (var component in coarse)
        {
            // After the alert's lock, as every change of a component's findings (Remediations.LockAsync).
            await remediations.LockAsync(component, cancellationToken);
        }

        var opened = await db.Findings.Where(f => f.EntryId == entry.Id && coarse.Contains(f.ComponentId)).ToListAsync(cancellationToken);
        foreach (var finding in opened.Where(f => MatchTypes.IsStrong(f.MatchType)))
        {
            await db.Entry(finding).ReloadAsync(cancellationToken);
            Open(finding, user, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        touched.UnionWith(await assessor.AssessAsync(entry, scope, cancellationToken));
        touched.UnionWith(targets);
        await remediations.SyncAsync(touched, user, cancellationToken);

        var reminderIds = await db.Findings.Where(f => f.EntryId == entry.Id && targets.Contains(f.ComponentId) && f.Status == "open" && f.RemediationId != null)
            .Select(f => f.RemediationId!.Value).ToListAsync(cancellationToken);
        var reminder = await db.Remediations.AsNoTracking().Where(r => reminderIds.Contains(r.Id)).OrderBy(r => r.Number)
            .Select(r => new { r.Id, r.Number }).FirstOrDefaultAsync(cancellationToken);
        notice.RemediationId = reminder?.Id;
        history.Add(EventKinds.NoticeAnswered, new
        {
            answer,
            entry = entry.Key,
            project = request.ProjectId,
            version,
            reminder = reminder?.Number,
            components = targets.Order().ToList(),
        }, actor: user, notice: notice.Id);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("notice.answered", "notice", notice.Id.ToString(), new { answer, entry = entry.Key, project = request.ProjectId, version, reminder = reminder?.Number }), cancellationToken);
        return notice;
    }

    public async Task<CommandResult<Notice>> ReopenAsync(Guid noticeId, ReopenNoticeRequest request, Guid user, CancellationToken cancellationToken)
    {
        if (request.ExpectedState is not (Notice.AffectsUs or Notice.NotAffected))
        {
            return Refusal.Validation("invalid_reopen", "The request is not valid.", new(StringComparer.Ordinal) { ["expectedState"] = ["Send the answer you saw: affects_us or not_affected."] });
        }

        var locked = await LockedAsync(noticeId, request.ExpectedState, cancellationToken);
        if (locked.Refusal is { } refused)
        {
            return refused;
        }

        var (notice, entry) = locked.Value;
        var now = time.GetUtcNow();
        var from = notice.State;

        // The findings the answer opened, unchanged since by a person (same person, same moment): they return to the notice.
        var openedByAnswer = db.Findings.Where(f => f.EntryId == notice.EntryId && f.Status == "open" && f.VerdictSource == "human"
            && f.StatusChangedBy == notice.DecidedBy && f.StatusChangedAt == notice.DecidedAt);
        var components = from != Notice.AffectsUs || notice.DecidedBy is null ? [] : await openedByAnswer.Select(f => f.ComponentId).Distinct().ToListAsync(cancellationToken);
        foreach (var component in components.Order())
        {
            await remediations.LockAsync(component, cancellationToken);
        }

        var returned = components.Count == 0 ? [] : await openedByAnswer.ToListAsync(cancellationToken);
        foreach (var finding in returned)
        {
            finding.StatusChangedBy = null;
            finding.StatusChangedAt = now;
            finding.Verdict = finding.VersionVerdict == "in_range" ? "affected" : finding.VersionVerdict == "out_of_range" ? "not_affected" : "uncertain";
            finding.VerdictSource = "rules";
            finding.ChangedAt = now;
        }

        notice.State = Notice.Open;
        notice.DecidedBy = null;
        notice.DecidedAt = null;
        notice.DecidedExploitation = null;
        notice.RemediationId = null;
        notice.ChangedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        history.Add(EventKinds.NoticeReopened, new { from, entry = entry.Key }, actor: user, notice: notice.Id);
        if (returned.Count > 0)
        {
            // The system closes them with the notice; a reminder left without open findings is withdrawn with them.
            var back = returned.Select(f => f.ComponentId).ToHashSet();
            var touched = new HashSet<Guid>(await assessor.AssessAsync(entry, AssessmentScope.ForComponents(back), cancellationToken));
            touched.UnionWith(back);
            await remediations.SyncAsync(touched, actor: null, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("notice.reopened", "notice", notice.Id.ToString(), new { from, entry = entry.Key }), cancellationToken);
        return notice;
    }

    /// <summary>
    /// The notice, read again under the alert's lock, which the assessment of its entry takes too: the state the caller saw
    /// is compared then and holds until the unit of work commits.
    /// </summary>
    private async Task<CommandResult<(Notice Notice, EntryFacts Entry)>> LockedAsync(Guid noticeId, string expectedState, CancellationToken cancellationToken)
    {
        var notice = await db.Notices.SingleOrDefaultAsync(n => n.Id == noticeId, cancellationToken);
        if (notice is null)
        {
            return NotFound();
        }

        await assessor.LockAsync(notice.EntryId, cancellationToken);
        await db.Entry(notice).ReloadAsync(cancellationToken);
        if (notice.State != expectedState)
        {
            return Refusal.Conflict("status_conflict", $"The notice is {notice.State} by now; reload it and answer again.");
        }

        var entry = await entries.FindAsync(notice.EntryId, cancellationToken);
        if (entry is null)
        {
            return NotFound();
        }

        return (notice, entry);
    }

    /// <summary>
    /// Where «affects us» puts the entry: the project's component of the product, found or recorded with the version; without
    /// a project, the stack entries the notice names (else every component it names), with the version recorded on them.
    /// </summary>
    private async Task<CommandResult<HashSet<Guid>>> PlaceAsync(Notice notice, Guid? projectId, string? version, CancellationToken cancellationToken)
    {
        var named = await profiles.FindComponentsAsync(notice.ComponentIds, cancellationToken);
        if (named.Count == 0)
        {
            return Refusal.Conflict("notice_without_component", "The components the notice named left the profile; nothing is left to remind about.");
        }

        IReadOnlyList<ProfileComponent> places;
        if (projectId is { } id)
        {
            var project = await profiles.FindProjectAsync(id, cancellationToken);
            if (project is null)
            {
                return Refusal.NotFound("project_not_found", "No such project.");
            }

            if (project.Archived)
            {
                return Refusal.Conflict("project_archived", "The project is archived.");
            }

            places = [named.FirstOrDefault(c => c.ProjectId == id) ?? named[0]];
        }
        else
        {
            var stack = named.Where(c => c.ProjectKind == "organization_stack").ToList();
            places = stack.Count > 0 ? stack : named;
        }

        if (version is not null && places.Any(c => c.Kind == "service"))
        {
            return Refusal.Validation("invalid_answer", "The answer is not valid.", new(StringComparer.Ordinal) { ["version"] = ["Services have no version."] });
        }

        var targets = new HashSet<Guid>();
        foreach (var like in places)
        {
            var placed = await placement.PlaceAsync(projectId ?? like.ProjectId, like.Id, version, cancellationToken);
            if (placed is null)
            {
                return Refusal.NotFound("project_not_found", "No such project.");
            }

            targets.Add(placed.ComponentId);
        }

        return targets;
    }

    private static void Decide(Notice notice, string state, Guid user, EntryFacts entry, DateTimeOffset now)
    {
        notice.State = state;
        notice.DecidedBy = user;
        notice.DecidedAt = now;
        notice.DecidedExploitation = entry.Exploitation;
        notice.ChangedAt = now;
    }

    /// <summary>A person's «affects us» on a coarse component: the finding opens with the verdict «affected» (master plan 17.3).</summary>
    private static void Open(Finding finding, Guid user, DateTimeOffset now)
    {
        finding.Status = "open";
        finding.StatusReason = null;
        finding.StatusChangedBy = user;
        finding.StatusChangedAt = now;
        finding.Verdict = "affected";
        finding.VerdictSource = "human";
        finding.ClosedLevel = null;
        finding.ClosedVersionVerdict = null;
        finding.Level = Math.Max(finding.Level, 2);
        // A person's decision, not part of a first fill: the reminder is announced like any other (master plan 7.7).
        finding.IsBackfill = false;
        finding.ChangedAt = now;
    }

    private static Refusal NotFound() => Refusal.NotFound("notice_not_found", "No such notice.");
}
