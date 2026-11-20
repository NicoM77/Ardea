using System.Text.Json;
using Ardea.Assessment.Contracts;
using Ardea.Assessment.Persistence;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Assessing;

internal static class EventKinds
{
    public const string ActionRaised = "action.raised";
    public const string ActionLevelRaised = "action.level_raised";
    public const string ActionExploitationEscalated = "action.exploitation_escalated";
    public const string ActionFixAvailable = "action.fix_available";
    public const string ActionLevelLowered = "action.level_lowered";
    public const string ActionWithdrawn = "action.withdrawn";

    public const string LegacyActionAssigned = "action.assigned";

    public const string ActionStatusChanged = "action.status_changed";
    public const string ActionNoteAdded = "action.note_added";
    public const string BackfillCompleted = "backfill.completed";

    public const string FindingJoined = "finding.joined";

    public const string FindingClosed = "finding.closed";

    public const string FindingReopened = "finding.reopened";

    public const string FindingStatusChanged = "finding.status_changed";

    public const string FindingVerdictConfirmed = "finding.verdict_confirmed";

    public const string FindingVerdictCorrected = "finding.verdict_corrected";

    public const string NoticeRaised = "notice.raised";

    public const string NoticeBackfilled = "notice.backfilled";

    public const string NoticeAnswered = "notice.answered";

    public const string NoticeReopened = "notice.reopened";

    public const string NoticeReappeared = "notice.reappeared";

    public const string NoticeExploited = "notice.exploited";

    public const string NoticeWithdrawn = "notice.withdrawn";

    public static bool IsNotifiable(string kind) =>
        kind.StartsWith("action.", StringComparison.Ordinal)
        || kind is BackfillCompleted or NoticeRaised or NoticeReappeared or NoticeExploited or NoticeBackfilled;
}

internal static class LegacyHistory
{
    private const string InReview = "in_review";

    public static bool Hidden(string kind, JsonElement data) =>
        kind == EventKinds.LegacyActionAssigned
        || (kind is EventKinds.ActionStatusChanged or EventKinds.FindingStatusChanged
            && ((Text(data, "to") == InReview && Text(data, "from") != "closed") || (Text(data, "from") == InReview && Text(data, "to") == "open")));

    public static bool ByMigration(string kind, JsonElement data) =>
        kind is EventKinds.ActionWithdrawn or EventKinds.FindingClosed && Text(data, "reason") == AlertAssessor.MovedToNotice;

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
        var events = _notifiable.ToList();
        _notifiable.Clear();
        foreach (var subscriber in subscribers)
        {
            await subscriber.OnActionEventsAsync(events, cancellationToken);
        }
    }
}

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
