using System.Text.Json;
using Ardea.Assessment.Persistence;
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
    public const string ActionAssigned = "action.assigned";
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
}

/// <summary>Writes history rows in the unit of work; the row's id is the event's id (master plan 8.2).</summary>
internal sealed class History(AssessDbContext db, TimeProvider time)
{
    public AssessEvent Add(string kind, object data, Guid? remediation = null, Guid? finding = null, Guid? project = null, Guid? actor = null)
    {
        var row = new AssessEvent
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            RemediationId = remediation,
            FindingId = finding,
            ProjectId = project,
            Actor = actor,
            At = time.GetUtcNow(),
            Data = JsonSerializer.SerializeToDocument(data, AlertAssessor.Json),
        };
        db.Events.Add(row);
        return row;
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
