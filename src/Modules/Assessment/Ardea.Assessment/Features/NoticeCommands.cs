using Ardea.Assessment.Assessing;
using Ardea.Assessment.Matching;
using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Platform.Audit;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Features;

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

        var targets = placed.Value!;
        Decide(notice, Notice.AffectsUs, user, entry, now);
        await db.SaveChangesAsync(cancellationToken);

        var scope = AssessmentScope.ForComponents(targets);
        var touched = new HashSet<Guid>(await assessor.AssessAsync(entry, scope, cancellationToken));
        var coarse = (await profiles.FindComponentsAsync(targets, cancellationToken)).Where(NoticeRules.IsCoarse).Select(c => c.Id).Order().ToArray();
        foreach (var component in coarse)
        {
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
        notice.DecidedKev = false;
        notice.RemediationId = null;
        notice.ChangedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        history.Add(EventKinds.NoticeReopened, new { from, entry = entry.Key }, actor: user, notice: notice.Id);

        var back = returned.Select(f => f.ComponentId).ToHashSet();
        var touched = new HashSet<Guid>(await assessor.AssessAsync(entry, AssessmentScope.ForComponents(back.Union(notice.ComponentIds).ToHashSet()), cancellationToken));
        touched.UnionWith(back);
        await remediations.SyncAsync(touched, actor: null, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("notice.reopened", "notice", notice.Id.ToString(), new { from, entry = entry.Key }), cancellationToken);
        return notice;
    }

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

    private async Task<CommandResult<HashSet<Guid>>> PlaceAsync(Notice notice, Guid? projectId, string? version, CancellationToken cancellationToken)
    {
        if (projectId is { } chosen)
        {
            var project = await profiles.FindProjectAsync(chosen, cancellationToken);
            if (project is null)
            {
                return Refusal.NotFound("project_not_found", "No such project.");
            }

            if (project.Archived)
            {
                return Refusal.Conflict("project_archived", "The project is archived.");
            }
        }

        var named = (await profiles.FindComponentsAsync(notice.ComponentIds, cancellationToken)).Where(c => NoticeRules.IsCoarse(c) && !c.ProjectArchived).ToList();
        if (named.Count == 0)
        {
            return Refusal.Conflict("notice_without_component", "The components the notice named left the profile or have an exact version now; nothing is left to remind about.");
        }

        IReadOnlyList<ProfileComponent> places;
        if (projectId is { } id)
        {
            places = [named.FirstOrDefault(c => c.ProjectId == id) ?? named[0]];
        }
        else
        {
            var stack = named.Where(c => c.ProjectKind == "organization_stack").ToList();
            places = stack.Count > 0 ? stack : named;

            if (version is not null && places.Count(c => c.ProjectKind != "organization_stack") > 1)
            {
                return Refusal.Validation("invalid_answer", "The answer is not valid.", new(StringComparer.Ordinal)
                {
                    ["projectId"] = ["The notice names several projects: choose the project the version is for."],
                });
            }
        }

        if (version is not null && places.Any(c => c.Kind == "service"))
        {
            return Refusal.Validation("invalid_answer", "The answer is not valid.", new(StringComparer.Ordinal) { ["version"] = ["Services have no version."] });
        }

        var targets = new HashSet<Guid>();
        foreach (var like in places)
        {
            var placed = await placement.PlaceAsync(projectId ?? like.ProjectId, like.Id, version, cancellationToken);
            switch (placed.Outcome)
            {
                case "archived":
                    return Refusal.Conflict("project_archived", "The project is archived.");
                case "repository":
                    return Refusal.Validation("invalid_answer", "The answer is not valid.", new(StringComparer.Ordinal)
                    {
                        ["version"] = ["A repository reads this product in the project: its version comes from there. Answer without a version, or detach the component first."],
                    });
                case not ("found" or "version_set" or "added"):
                    return Refusal.NotFound("project_not_found", "No such project.");
            }

            targets.Add(placed.ComponentId!.Value);
        }

        return targets;
    }

    private static void Decide(Notice notice, string state, Guid user, EntryFacts entry, DateTimeOffset now)
    {
        notice.State = state;
        notice.DecidedBy = user;
        notice.DecidedAt = now;
        notice.DecidedExploitation = entry.Exploitation;
        notice.DecidedKev = entry.InKev;
        notice.ChangedAt = now;
    }

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
        finding.IsBackfill = false;
        finding.ChangedAt = now;
    }

    private static Refusal NotFound() => Refusal.NotFound("notice_not_found", "No such notice.");
}
