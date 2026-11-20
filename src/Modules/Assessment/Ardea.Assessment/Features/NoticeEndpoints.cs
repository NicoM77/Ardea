using System.Globalization;
using System.Text;
using Ardea.Assessment.Persistence;
using Ardea.Platform.Http;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Features;

/// <summary>
/// Notices (master plan 17.3, 17.7; ADR 047): the inbox «could affect you», its answers and taking an answer back. Every
/// member reads; members and up answer. The page comes with task 5a.5.
/// </summary>
internal static class NoticeEndpoints
{
    private const int MaxLimit = 100;

    public static void Map(IEndpointRouteBuilder api)
    {
        var org = api.MapOrganization().WithTags("Notices");
        org.MapGet("/notices", ListAsync).WithName("listNotices").Produces<ListPage<NoticeView>>().Problems(400, 403, 404);
        org.MapGet("/notices/{noticeId:guid}", GetAsync).WithName("getNotice").Produces<NoticeView>().Problems(403, 404);
        org.MapPost("/notices/{noticeId:guid}/answer", AnswerAsync).WithName("answerNotice").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<NoticeView>().Problems(400, 403, 404, 409);
        org.MapPost("/notices/{noticeId:guid}/reopen", ReopenAsync).WithName("reopenNotice").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<NoticeView>().Problems(400, 403, 404, 409);
    }

    /// <summary>
    /// Notices, actively exploited first, then by CVSS score, then newest (E23). <c>state</c>: <c>new</c> (default: open, raised
    /// as an entry arrived), <c>earlier</c> (open, raised at once when a stack entry was added or changed, from the half year
    /// before; ADR 047), <c>answered</c> (affects us or doesn't), <c>withdrawn</c>; <c>component</c> narrows to the notices a stack entry or
    /// another coarse component raised.
    /// </summary>
    internal static async Task<IResult> ListAsync(
        AssessDbContext db, NoticeReader reader, string? state, Guid? component, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var after = Cursor(cursor, errors);
        if (state is not (null or "new" or "earlier" or "answered" or "withdrawn"))
        {
            errors["state"] = ["Choose new, earlier, answered or withdrawn."];
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation("invalid_filter", "The filter is not valid.", errors);
        }

        var query = db.Notices.AsNoTracking();
        query = (state ?? "new") switch
        {
            "new" => query.Where(n => n.State == Notice.Open && !n.IsBackfill),
            "earlier" => query.Where(n => n.State == Notice.Open && n.IsBackfill),
            "answered" => query.Where(n => n.State == Notice.AffectsUs || n.State == Notice.NotAffected),
            _ => query.Where(n => n.State == Notice.Withdrawn),
        };
        if (component is { } id)
        {
            query = query.Where(n => n.ComponentIds.Contains(id));
        }

        if (after is { } c)
        {
            var exploited = c.Exploited == 1;
            query = query.Where(n => (exploited && !n.Exploited)
                || (n.Exploited == exploited && (n.ScoreRank < c.Score
                    || (n.ScoreRank == c.Score && (n.RaisedAt < c.RaisedAt || (n.RaisedAt == c.RaisedAt && n.Id.CompareTo(c.Id) < 0))))));
        }

        var size = Math.Clamp(limit ?? 50, 1, MaxLimit);
        var rows = await query
            .OrderByDescending(n => n.Exploited).ThenByDescending(n => n.ScoreRank).ThenByDescending(n => n.RaisedAt).ThenByDescending(n => n.Id)
            .Take(size + 1).ToListAsync(cancellationToken);
        var page = rows.Take(size).ToList();
        var next = rows.Count > size ? WriteCursor(page[^1]) : null;
        return Results.Ok(new ListPage<NoticeView>(await reader.ViewsAsync(page, cancellationToken), next));
    }

    private static async Task<IResult> GetAsync(Guid noticeId, AssessDbContext db, NoticeReader reader, CancellationToken cancellationToken) =>
        await db.Notices.AsNoTracking().SingleOrDefaultAsync(n => n.Id == noticeId, cancellationToken) is { } notice
            ? Results.Ok((await reader.ViewsAsync([notice], cancellationToken))[0])
            : ApiProblems.Problem("notice_not_found", StatusCodes.Status404NotFound, "No such notice.");

    private static async Task<IResult> AnswerAsync(
        Guid noticeId, AnswerNoticeRequest request, HttpContext http, NoticeCommands commands, NoticeReader reader, IUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        await RespondAsync(await commands.AnswerAsync(noticeId, request, http.OrganizationAccess().UserId, cancellationToken), reader, unitOfWork, cancellationToken);

    private static async Task<IResult> ReopenAsync(
        Guid noticeId, ReopenNoticeRequest request, HttpContext http, NoticeCommands commands, NoticeReader reader, IUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        await RespondAsync(await commands.ReopenAsync(noticeId, request, http.OrganizationAccess().UserId, cancellationToken), reader, unitOfWork, cancellationToken);

    /// <summary>The view is read before the commit, never after (ADR 023).</summary>
    private static async Task<IResult> RespondAsync(CommandResult<Notice> result, NoticeReader reader, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        if (result.Refusal is { } refusal)
        {
            return refusal.ToResult();
        }

        var view = (await reader.ViewsAsync([result.Value!], cancellationToken))[0];
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(view);
    }

    private static string WriteCursor(Notice n) => Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Create(
        CultureInfo.InvariantCulture,
        $"{(n.Exploited ? 1 : 0)}|{n.ScoreRank.ToString("R", CultureInfo.InvariantCulture)}|{n.RaisedAt.UtcTicks}|{n.Id}")));

    private static (int Exploited, double Score, DateTimeOffset RaisedAt, Guid Id)? Cursor(string? cursor, Dictionary<string, string[]> errors)
    {
        if (cursor is null)
        {
            return null;
        }

        try
        {
            if (Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|') is [var e, var s, var r, var i]
                && int.TryParse(e, NumberStyles.None, CultureInfo.InvariantCulture, out var exploited)
                && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var score)
                && long.TryParse(r, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && ticks is >= 0 and <= 3155378975999999999
                && Guid.TryParse(i, out var id))
            {
                return (exploited, score, new DateTimeOffset(ticks, TimeSpan.Zero), id);
            }
        }
        catch (FormatException)
        {
        }

        errors["cursor"] = ["Start again from the first page."];
        return null;
    }
}
