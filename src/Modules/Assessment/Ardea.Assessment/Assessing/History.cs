using System.Text.Json;
using Ardea.Assessment.Contracts;
using Ardea.Assessment.Persistence;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Assessing;

/// <summary>The kinds of history rows; the <c>action.*</c> kinds and <c>backfill.completed</c> are the events of master plan 8.2.</summary>
internal static class EventKinds
{
    public const string ActionRaised = "action.raised";
    public const string ActionLevelRaised = "action.level_raised";
    public const string ActionExploitationEscalated = "action.exploitation_escalated";
    public const string ActionFixAvailable = "action.fix_available";
    public const string ActionLevelLowered = "action.level_lowered";
    public const string ActionWithdrawn = "action.withdrawn";

    /// <summary>Written until ADR 046 removed the assignment; such rows stay in the table but are never shown or announced.</summary>
    public const string LegacyActionAssigned = "action.assigned";

    public const string ActionStatusChanged = "action.status_changed";
    public const string ActionNoteAdded = "action.note_added";
    public const string BackfillCompleted = "backfill.completed";

    /// <summary>A finding joined an action; history only.</summary>
    public const string FindingJoined = "finding.joined";

    /// <summary>A finding closed without a person: fixed by update, withdrawn, no longer matched, …; history only.</summary>
    public const string FindingClosed = "finding.closed";

    /// <summary>A finding a person or the system had closed is open again (master plan 6.7); history only.</summary>
    public const string FindingReopened = "finding.reopened";

    /// <summary>A person set the outcome of a finding; history only.</summary>
    public const string FindingStatusChanged = "finding.status_changed";

    /// <summary>A person confirmed the model's estimate of a finding (task 4.7); history only.</summary>
    public const string FindingVerdictConfirmed = "finding.verdict_confirmed";

    /// <summary>A person corrected the verdict or the level of a finding with a reason (task 4.7); history only.</summary>
    public const string FindingVerdictCorrected = "finding.verdict_corrected";

    /// <summary>
    /// A notice was raised (ADR 047): the seam for the event <c>notice.raised</c> of task 5a.6. History only until then, as
    /// are the other <c>notice.*</c> kinds.
    /// </summary>
    public const string NoticeRaised = "notice.raised";

    /// <summary>A reverse search raised notices at once, when a stack entry was added or changed: one row instead of many.</summary>
    public const string NoticeBackfilled = "notice.backfilled";

    /// <summary>A person answered a notice: «affects us» or «doesn't affect us».</summary>
    public const string NoticeAnswered = "notice.answered";

    /// <summary>A person took an answer back; the notice is open again.</summary>
    public const string NoticeReopened = "notice.reopened";

    /// <summary>A notice answered «doesn't affect us» came back once, the entry being exploited now (E22).</summary>
    public const string NoticeReappeared = "notice.reappeared";

    /// <summary>An open notice nothing coarse names any more: the precise path took over, or its components left.</summary>
    public const string NoticeWithdrawn = "notice.withdrawn";

    /// <summary>The events of master plan 8.2 the notifications hear about; the other kinds are history only.</summary>
    public static bool IsNotifiable(string kind) => kind.StartsWith("action.", StringComparison.Ordinal) || kind == BackfillCompleted;
}

/// <summary>
/// History rows written before the reminder lost its assignee and its review state (master plan 17.5, ADR 046). They
/// stay in the table, as history is never deleted (master plan 4.10), but reads leave out what no longer exists:
/// assignments, and taking an open action into review or giving it back. Wherever else «in review» appears, it reads
/// as «open»: the migration of ADR 046 made every action and finding in review open.
/// </summary>
internal static class LegacyHistory
{
    private const string InReview = "in_review";

    /// <summary>A row the history and the notifications leave out.</summary>
    public static bool Hidden(string kind, JsonElement data) =>
        kind == EventKinds.LegacyActionAssigned
        || (kind is EventKinds.ActionStatusChanged or EventKinds.FindingStatusChanged
            && ((Text(data, "to") == InReview && Text(data, "from") != "closed") || (Text(data, "from") == InReview && Text(data, "to") == "open")));

    /// <summary>A closing the migration of ADR 046 wrote when it turned an action into notices: history, not a change of the day.</summary>
    public static bool ByMigration(string kind, JsonElement data) =>
        kind is EventKinds.ActionWithdrawn or EventKinds.FindingClosed && Text(data, "reason") == AlertAssessor.MovedToNotice;

    /// <summary>The row's data as it reads today: «in review» in <c>from</c> or <c>to</c> is «open».</summary>
    public static JsonElement Shown(JsonElement data)
    {
        if (Text(data, "from") != InReview && Text(data, "to") != InReview)
        {
            return data.Clone();
        }

        var node = System.Text.Json.Nodes.JsonNode.Parse(data.GetRawText())!.AsObject();
        foreach (var property in new[] { "from", "to" })
        {
            if (Text(data, property) == InReview)
            {
                node[property] = "open";
            }
        }

        return JsonSerializer.SerializeToElement(node);
    }

    private static string? Text(JsonElement data, string property) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// Writes history rows in the unit of work; the row's id is the event's id (master plan 8.2). The events
/// of 8.2 go to the subscribers right before the unit of work commits, so their follow-up jobs commit with them.
/// </summary>
internal sealed class History(AssessDbContext db, TimeProvider time, IUnitOfWork unitOfWork, IEnumerable<IActionEventSubscriber> subscribers)
{
    private readonly List<NotifiableEvent> _notifiable = [];

    public AssessEvent Add(string kind, object data, Guid? remediation = null, Guid? finding = null, Guid? project = null, Guid? actor = null, Guid? notice = null)
    {
        var row = new AssessEvent
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            RemediationId = remediation,
            FindingId = finding,
            ProjectId = project,
            NoticeId = notice,
            Actor = actor,
            At = time.GetUtcNow(),
            Data = JsonSerializer.SerializeToDocument(data, AlertAssessor.Json),
        };
        db.Events.Add(row);
        if (EventKinds.IsNotifiable(kind))
        {
            if (_notifiable.Count == 0)
            {
                unitOfWork.BeforeCommit(HandOnAsync);
            }

            _notifiable.Add(new NotifiableEvent(row.Id, row.Kind, row.At));
        }

        return row;
    }

    private async Task HandOnAsync(CancellationToken cancellationToken)
    {
        // A row added but never saved does not exist; subscribers read events by id and skip unknown ones.
        var events = _notifiable.ToList();
        _notifiable.Clear();
        foreach (var subscriber in subscribers)
        {
            await subscriber.OnActionEventsAsync(events, cancellationToken);
        }
    }
}

/// <summary>
/// The organisation's number range, shared by actions and alerts (master plan 4.9): raised under the
/// counter row's lock in the writing transaction, so a number is handed out once.
/// </summary>
internal sealed class Numbers(AssessDbContext db, ITenantContext tenant)
{
    public async Task<int> NextAsync(CancellationToken cancellationToken)
    {
        var organization = tenant.RequiredOrganizationId;
        var numbers = await db.Database.SqlQuery<int>($"""
            INSERT INTO assess.org_counter (organization_id, next_number) VALUES ({organization}, 2)
            ON CONFLICT (organization_id) DO UPDATE SET next_number = org_counter.next_number + 1
            RETURNING next_number - 1 AS "Value"
            """).ToListAsync(cancellationToken);
        return numbers[0];
    }
}
