using System.Globalization;
using System.Text;
using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Platform.Http;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Features;

/// <summary>Why a command did not run, as problem details (master plan 10.1).</summary>
internal sealed record Refusal(string Code, int Status, string Title, Dictionary<string, string[]>? Errors = null)
{
    public static Refusal Validation(string code, string title, Dictionary<string, string[]> errors) => new(code, StatusCodes.Status400BadRequest, title, errors);

    public static Refusal NotFound(string code, string title) => new(code, StatusCodes.Status404NotFound, title);

    public static Refusal Conflict(string code, string title) => new(code, StatusCodes.Status409Conflict, title);

    public IResult ToResult() => ApiProblems.Problem(Code, Status, Title, Errors);
}

/// <summary>A command's value or why it was refused.</summary>
internal readonly record struct CommandResult<T>(T? Value, Refusal? Refusal)
{
    public static implicit operator CommandResult<T>(T value) => new(value, null);

    public static implicit operator CommandResult<T>(Refusal refusal) => new(default, refusal);
}

/// <summary>
/// Actions and alerts (master plan 10.2 «Massnahmen und Meldungen»): the read side the overview, the list
/// and the details build on (tasks 3.8 to 3.10), status changes with the expected starting status, and notes.
/// An action is open or closed; it has no assignee and no deadline (master plan 17.5, ADR 046). Every member
/// reads; members and up change.
/// </summary>
internal static class ActionEndpoints
{
    private const int MaxLimit = 100;

    public static void Map(IEndpointRouteBuilder api)
    {
        var org = api.MapOrganization().WithTags("Actions");
        org.MapGet("/overview", OverviewAsync).WithName("getOverview").Produces<OverviewView>().Problems(403, 404);
        org.MapGet("/actions", ListAsync).WithName("listActions").Produces<ListPage<ActionSummary>>().Problems(400, 403, 404);
        org.MapGet("/actions/{number:int}", GetAsync).WithName("getAction").Produces<ActionDetailView>().Problems(403, 404);
        org.MapPost("/actions/{number:int}/status", SetStatusAsync).WithName("setActionStatus").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<ActionDetailView>().Problems(400, 403, 404, 409);
        org.MapGet("/actions/{number:int}/events", EventsAsync).WithName("listActionEvents").Produces<IReadOnlyList<ActionEventView>>().Problems(403, 404);
        org.MapPost("/actions/{number:int}/notes", AddNoteAsync).WithName("addActionNote").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<ActionEventView>(StatusCodes.Status201Created).Problems(400, 403, 404);
        org.MapGet("/findings", FindingsAsync).WithName("listFindings").Produces<ListPage<FindingView>>().Problems(400, 403, 404);
        org.MapPatch("/findings/{findingId:guid}", SetFindingStatusAsync).WithName("setFindingStatus").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<FindingView>().Problems(400, 403, 404, 409);
        org.MapPost("/findings/{findingId:guid}/review", ReviewFindingAsync).WithName("reviewFinding").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<FindingView>().Problems(400, 403, 404, 409);
        org.MapGet("/alerts", AlertsAsync).WithName("listAlerts").Produces<ListPage<AlertSummary>>().Problems(400, 403, 404);
        org.MapGet("/alerts/{number:int}", AlertAsync).WithName("getAlert").Produces<AlertDetailView>().Problems(403, 404);
        org.MapPost("/alerts/{number:int}/assessment", RequestAssessmentAsync).WithName("requestAlertAssessment").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<AssessmentRequestView>(StatusCodes.Status202Accepted).Problems(403, 404, 409, 429);
    }

    /// <summary>
    /// A person asks for the model's assessment of an alert (master plan 7.5; task 4.3): without threshold and
    /// without the actions' quota, but within the daily token quotas and the daily cap on requests by people. The job runs
    /// before every waiting assessment of the system, under a key of its own, so a waiting system job never swallows it.
    /// </summary>
    private static async Task<IResult> RequestAssessmentAsync(
        int number, HttpContext http, AssessDbContext db, Estimating.EstimateTrigger trigger, IJobQueue jobs, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        var alert = await db.Alerts.AsNoTracking().SingleOrDefaultAsync(a => a.Number == number, cancellationToken);
        if (alert is null)
        {
            return ApiProblems.Problem("alert_not_found", StatusCodes.Status404NotFound, "No such alert.");
        }

        if ((await trigger.AllowedAsync(cancellationToken)).Count == 0)
        {
            return ApiProblems.Problem("ai_not_available", StatusCodes.Status409Conflict, "No language model is available under the organisation's AI data policy.");
        }

        if (!await trigger.PersonRequestsLeftAsync(cancellationToken))
        {
            return ApiProblems.Problem("assessment_requests_exhausted", StatusCodes.Status429TooManyRequests, "The organisation has used today's requests for assessments.");
        }

        var access = http.OrganizationAccess();
        var queued = await jobs.EnqueueAsync(
            new Estimating.EstimateAlert(alert.EntryId, access.UserId),
            Estimating.EstimateAlert.Options(access.OrganizationId, alert.EntryId, alert.Level, person: true),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Accepted((string?)null, new AssessmentRequestView(queued));
    }

    /// <summary>
    /// Actions, highest level first. <c>status</c>: <c>active</c> (default, the same as <c>open</c>), <c>open</c>,
    /// <c>closed</c>, <c>all</c>; <c>level</c> and <c>project</c> take comma-separated values;
    /// <c>q</c> finds a number, an entry key or a component, package or project name.
    /// </summary>
    private static async Task<IResult> ListAsync(
        AssessDbContext db, ActionReader reader, IProfileQueries profiles, string? status, string? level, string? project, string? q, int? limit, string? cursor,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var levels = Ints(level, "level", errors);
        var projects = Guids(project, "project", errors);
        var after = Cursor(cursor, errors);
        if (status is not (null or "active" or "open" or "closed" or "all"))
        {
            errors["status"] = ["Choose active, open, closed or all."];
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation("invalid_filter", "The filter is not valid.", errors);
        }

        var query = db.Remediations.AsNoTracking();
        query = (status ?? "active") switch
        {
            "active" => query.Where(r => r.Status != "closed"),
            "all" => query,
            var s => query.Where(r => r.Status == s),
        };
        if (levels.Length > 0)
        {
            query = query.Where(r => levels.Contains(r.Level));
        }

        if (projects.Length > 0)
        {
            query = query.Where(r => projects.Contains(r.ProjectId));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var text = q.Trim().TrimStart('#');
            var number = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
            var pattern = "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var byEntry = from f in db.Findings join a in db.Alerts on f.AlertId equals a.Id where EF.Functions.ILike(a.EntryKey, pattern) && f.RemediationId != null select f.RemediationId!.Value;
            var components = (await profiles.SearchComponentsAsync(text, 1000, cancellationToken)).ToArray();
            query = query.Where(r => r.Number == number || byEntry.Contains(r.Id) || components.Contains(r.ComponentId));
        }

        if (after is { } c)
        {
            query = query.Where(r => r.Level < c.Level || (r.Level == c.Level && r.Number < c.Number));
        }

        var size = Math.Clamp(limit ?? 50, 1, MaxLimit);
        var rows = await query.OrderByDescending(r => r.Level).ThenByDescending(r => r.Number).Take(size + 1).ToListAsync(cancellationToken);
        var page = rows.Take(size).ToList();
        var next = rows.Count > size ? WriteCursor(page[^1].Level, page[^1].Number) : null;
        return Results.Ok(new ListPage<ActionSummary>(await reader.SummariesAsync(page, cancellationToken), next));
    }

    private static async Task<IResult> GetAsync(int number, AssessDbContext db, ActionReader reader, CancellationToken cancellationToken) =>
        await db.Remediations.AsNoTracking().SingleOrDefaultAsync(r => r.Number == number, cancellationToken) is { } action
            ? Results.Ok(await DetailAsync(action, db, reader, cancellationToken))
            : ActionNotFound();

    private static async Task<IResult> SetStatusAsync(
        int number, SetActionStatusRequest request, HttpContext http, ActionCommands commands, AssessDbContext db, ActionReader reader, IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var result = await commands.SetActionStatusAsync(number, request, http.OrganizationAccess().UserId, cancellationToken);
        return await RespondAsync(result, db, reader, unitOfWork, cancellationToken);
    }

    private static async Task<IResult> EventsAsync(int number, AssessDbContext db, ActionReader reader, CancellationToken cancellationToken) =>
        await db.Remediations.AsNoTracking().SingleOrDefaultAsync(r => r.Number == number, cancellationToken) is { } action
            ? Results.Ok(await reader.EventsAsync(action.Id, cancellationToken))
            : ActionNotFound();

    private static async Task<IResult> AddNoteAsync(
        int number, AddNoteRequest request, HttpContext http, ActionCommands commands, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        var result = await commands.AddNoteAsync(number, request, http.OrganizationAccess().UserId, cancellationToken);
        if (result.Refusal is { } refusal)
        {
            return refusal.ToResult();
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Created((string?)null, ActionReader.View(result.Value!));
    }

    /// <summary>Findings, highest level first, also those of level 1 for the view «Not relevant».</summary>
    private static async Task<IResult> FindingsAsync(
        AssessDbContext db, ActionReader reader, string? level, string? project, string? status, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var levels = Ints(level, "level", errors);
        var projects = Guids(project, "project", errors);
        var after = GuidCursor(cursor, errors);
        var statuses = status?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        if (statuses.Any(s => s is not ("open" or "remediated" or "risk_accepted" or "not_affected")))
        {
            errors["status"] = ["Choose from open, remediated, risk_accepted, not_affected."];
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation("invalid_filter", "The filter is not valid.", errors);
        }

        var query = db.Findings.AsNoTracking();
        if (levels.Length > 0)
        {
            query = query.Where(f => levels.Contains(f.Level));
        }

        if (projects.Length > 0)
        {
            query = query.Where(f => projects.Contains(f.ProjectId));
        }

        if (statuses.Length > 0)
        {
            query = query.Where(f => statuses.Contains(f.Status));
        }

        if (after is { } c)
        {
            query = query.Where(f => f.Level < c.Level || (f.Level == c.Level && f.Id.CompareTo(c.Id) < 0));
        }

        var size = Math.Clamp(limit ?? 50, 1, MaxLimit);
        var rows = await query.OrderByDescending(f => f.Level).ThenByDescending(f => f.Id).Take(size + 1).ToListAsync(cancellationToken);
        var page = rows.Take(size).ToList();
        var next = rows.Count > size ? WriteCursor(page[^1].Level, page[^1].Id) : null;
        return Results.Ok(new ListPage<FindingView>(await reader.FindingsAsync(page, cancellationToken), next));
    }

    private static async Task<IResult> SetFindingStatusAsync(
        Guid findingId, SetFindingStatusRequest request, HttpContext http, ActionCommands commands, ActionReader reader, IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var result = await commands.SetFindingStatusAsync(findingId, request, http.OrganizationAccess().UserId, cancellationToken);
        if (result.Refusal is { } refusal)
        {
            return refusal.ToResult();
        }

        var view = (await reader.FindingsAsync([result.Value!], cancellationToken))[0];
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(view);
    }

    /// <summary>
    /// A person confirms the model's estimate of a finding or corrects its verdict or level (task 4.7, master plan 7.9).
    /// The finding is assessed again in the same unit of work; the answer is the finding as it is then.
    /// </summary>
    private static async Task<IResult> ReviewFindingAsync(
        Guid findingId, ReviewVerdictRequest request, HttpContext http, VerdictReviews reviews, ActionReader reader, IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var result = await reviews.ReviewAsync(findingId, request, http.OrganizationAccess().UserId, cancellationToken);
        if (result.Refusal is { } refusal)
        {
            return refusal.ToResult();
        }

        var view = (await reader.FindingsAsync([result.Value!], cancellationToken))[0];
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(view);
    }

    /// <summary>Alerts with a number (a finding reached level 2 once), highest level first; <c>q</c> finds an entry key.</summary>
    private static async Task<IResult> AlertsAsync(
        AssessDbContext db, ActionReader reader, string? level, string? q, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var levels = Ints(level, "level", errors);
        var after = Cursor(cursor, errors);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation("invalid_filter", "The filter is not valid.", errors);
        }

        var query = db.Alerts.AsNoTracking().Where(a => a.Number != null);
        if (levels.Length > 0)
        {
            query = query.Where(a => levels.Contains(a.Level));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var text = q.Trim().TrimStart('#');
            var number = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
            var pattern = "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(a => a.Number == number || EF.Functions.ILike(a.EntryKey, pattern));
        }

        if (after is { } c)
        {
            query = query.Where(a => a.Level < c.Level || (a.Level == c.Level && a.Number < c.Number));
        }

        var size = Math.Clamp(limit ?? 50, 1, MaxLimit);
        var rows = await query.OrderByDescending(a => a.Level).ThenByDescending(a => a.Number).Take(size + 1).ToListAsync(cancellationToken);
        var page = rows.Take(size).ToList();
        var next = rows.Count > size ? WriteCursor(page[^1].Level, page[^1].Number!.Value) : null;
        return Results.Ok(new ListPage<AlertSummary>(await reader.AlertSummariesAsync(page, cancellationToken), next));
    }

    private static async Task<IResult> AlertAsync(
        int number, AssessDbContext db, ActionReader reader, Intel.Contracts.IEntryQueries entries, CancellationToken cancellationToken)
    {
        var alert = await db.Alerts.AsNoTracking().SingleOrDefaultAsync(a => a.Number == number, cancellationToken);
        if (alert is null)
        {
            return ApiProblems.Problem("alert_not_found", StatusCodes.Status404NotFound, "No such alert.");
        }

        var entry = await entries.FindAsync(alert.EntryId, cancellationToken);
        var findings = await db.Findings.AsNoTracking().Where(f => f.AlertId == alert.Id).OrderByDescending(f => f.Level).ThenBy(f => f.Id).ToListAsync(cancellationToken);
        var actionIds = findings.Select(f => f.RemediationId).OfType<Guid>().Distinct().ToArray();
        var actions = await db.Remediations.AsNoTracking().Where(r => actionIds.Contains(r.Id)).OrderByDescending(r => r.Level).ThenByDescending(r => r.Number).ToListAsync(cancellationToken);
        var entryView = entry is null
            ? new AlertEntryView(alert.EntryId, alert.EntryKey, null, "unknown", null, alert.ChangedAt, null, null, null, "none", null, null, false, null, false)
            : new AlertEntryView(
                entry.Id, entry.Key, entry.Title, entry.State, entry.PublishedAt, entry.ChangedAt, entry.CvssScore, entry.CvssVersion, entry.CvssVector,
                entry.Exploitation, entry.ExploitationSource, entry.ExploitationSince, entry.FixAvailable, entry.EpssClass, entry.AuthorityWarning);
        return Results.Ok(new AlertDetailView(
            (await reader.AlertSummariesAsync([alert], cancellationToken))[0], entryView, await reader.AnswersAsync(findings, null, cancellationToken),
            await reader.SummariesAsync(actions, cancellationToken), await reader.FindingsAsync(findings, cancellationToken)));
    }

    private static async Task<IResult> OverviewAsync(AssessDbContext db, ActionReader reader, IProfileQueries profiles, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var open = await db.Remediations.AsNoTracking().Where(r => r.Status != "closed").ToListAsync(cancellationToken);
        var byLevel = new[] { 5, 4, 3, 2 }.Select(l => new LevelCountView(l, Levels.Name(l), open.Count(r => r.Level == l))).ToList();
        // Highest level first; within a level, what has waited longest at it (master plan 17.5: no deadlines).
        var urgent = open.OrderByDescending(r => r.Level).ThenBy(r => r.LevelRaisedAt ?? r.OpenedAt).ThenBy(r => r.Number).Take(5).ToList();

        var names = (await profiles.FindComponentsAsync([.. open.Select(r => r.ComponentId).Distinct()], cancellationToken))
            .GroupBy(c => c.ProjectId).ToDictionary(g => g.Key, g => g.First().ProjectName);
        var projects = open.GroupBy(r => r.ProjectId)
            .Select(g => new ProjectLevelView(new ActionProjectRef(g.Key, names.GetValueOrDefault(g.Key) ?? string.Empty), g.Max(r => r.Level), Levels.Name(g.Max(r => r.Level)), g.Count()))
            .OrderByDescending(p => p.Level).ThenBy(p => p.Project.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        var since = now.AddDays(-1);
        // Without the rows the history no longer shows (ADR 046) and without the migration's closings, which are no change
        // anyone made: the overview shows no burst after the upgrade.
        var changes = (await db.Events.AsNoTracking()
                .Where(e => e.At >= since && ((e.Kind.StartsWith("action.") && e.Kind != Assessing.EventKinds.LegacyActionAssigned) || e.Kind == "backfill.completed"))
                .Select(e => new { e.Kind, e.Data })
                .ToListAsync(cancellationToken))
            .Where(e => !Assessing.LegacyHistory.Hidden(e.Kind, e.Data.RootElement) && !Assessing.LegacyHistory.ByMigration(e.Kind, e.Data.RootElement))
            .GroupBy(e => e.Kind, StringComparer.Ordinal).Select(g => new ChangeCountView(g.Key, g.Count())).ToList();
        // The urgent actions open with their three answers (master plan 11.2, phase 3 acceptance).
        var urgentIds = urgent.Select(r => (Guid?)r.Id).ToArray();
        var urgentFindings = (await db.Findings.AsNoTracking().Where(f => urgentIds.Contains(f.RemediationId)).ToListAsync(cancellationToken)).ToLookup(f => f.RemediationId!.Value);
        var answers = await reader.AnswersAsync([.. urgent.Select(r => ((IReadOnlyList<Finding>)[.. urgentFindings[r.Id]], r.TargetVersion))], cancellationToken);
        var summaries = await reader.SummariesAsync(urgent, cancellationToken);
        return Results.Ok(new OverviewView(
            byLevel, [.. summaries.Zip(answers, (s, a) => new UrgentActionView(s, a))], projects,
            [.. changes.OrderBy(c => c.Kind, StringComparer.Ordinal)]));
    }

    private static async Task<IResult> RespondAsync(
        CommandResult<Remediation> result, AssessDbContext db, ActionReader reader, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        if (result.Refusal is { } refusal)
        {
            return refusal.ToResult();
        }

        var detail = await DetailAsync(result.Value!, db, reader, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(detail);
    }

    private static async Task<ActionDetailView> DetailAsync(Remediation action, AssessDbContext db, ActionReader reader, CancellationToken cancellationToken)
    {
        var findings = await db.Findings.AsNoTracking().Where(f => f.RemediationId == action.Id)
            .OrderByDescending(f => f.Status == "open").ThenByDescending(f => f.Level).ThenBy(f => f.Id)
            .ToListAsync(cancellationToken);
        return new ActionDetailView(
            (await reader.SummariesAsync([action], cancellationToken))[0], await reader.AnswersAsync(findings, action.TargetVersion, cancellationToken),
            await reader.FindingsAsync(findings, cancellationToken), await reader.EventsAsync(action.Id, cancellationToken));
    }

    private static IResult ActionNotFound() => ApiProblems.Problem("action_not_found", StatusCodes.Status404NotFound, "No such action.");

    private static int[] Ints(string? text, string field, Dictionary<string, string[]> errors)
    {
        var parts = text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var values = new List<int>();
        foreach (var part in parts)
        {
            if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 1 and <= 5)
            {
                values.Add(value);
            }
            else
            {
                errors[field] = ["Levels from 1 to 5, separated by commas."];
            }
        }

        return [.. values];
    }

    private static Guid[] Guids(string? text, string field, Dictionary<string, string[]> errors)
    {
        var parts = text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var values = new List<Guid>();
        foreach (var part in parts)
        {
            if (Guid.TryParse(part, out var value))
            {
                values.Add(value);
            }
            else
            {
                errors[field] = ["Project ids, separated by commas."];
            }
        }

        return [.. values];
    }

    private static string WriteCursor(int level, object key) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{level}|{key}")));

    private static (int Level, int Number)? Cursor(string? cursor, Dictionary<string, string[]> errors)
    {
        if (cursor is null)
        {
            return null;
        }

        if (Parts(cursor) is [var level, var number]
            && int.TryParse(level, NumberStyles.None, CultureInfo.InvariantCulture, out var l) && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            return (l, n);
        }

        errors["cursor"] = ["Start again from the first page."];
        return null;
    }

    private static (int Level, Guid Id)? GuidCursor(string? cursor, Dictionary<string, string[]> errors)
    {
        if (cursor is null)
        {
            return null;
        }

        if (Parts(cursor) is [var level, var id] && int.TryParse(level, NumberStyles.None, CultureInfo.InvariantCulture, out var l) && Guid.TryParse(id, out var g))
        {
            return (l, g);
        }

        errors["cursor"] = ["Start again from the first page."];
        return null;
    }

    private static string[]? Parts(string cursor)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
