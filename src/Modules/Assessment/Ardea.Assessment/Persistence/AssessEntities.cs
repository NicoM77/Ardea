using System.Text.Json;
using Ardea.Platform.Persistence;

namespace Ardea.Assessment.Persistence;

/// <summary>A vulnerability from the point of view of one organisation (master plan 4.6, 7.4).</summary>
internal sealed class Alert : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid EntryId { get; set; }

    /// <summary>The maximum over its findings; 1 while none of them is relevant.</summary>
    public int Level { get; set; }

    /// <summary>The entry's revision the findings were last computed from.</summary>
    public int AssessedEntryRevision { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>
/// One component affected by one entry (master plan 4.6, 7.2, 7.3). Weak matches and versions outside
/// the affected ranges are stored too, as level 1 with status <c>not_affected</c>: they answer why an
/// alert did not come.
/// </summary>
internal sealed class Finding : IOrganizationOwned
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid AlertId { get; set; }

    public Guid EntryId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ComponentId { get; set; }

    /// <summary>
    /// How the entry hit the component (master plan 7.2): strong <c>package</c>, <c>cpe</c>, <c>product</c>;
    /// weak <c>variant</c>, <c>ecosystem</c>, <c>name</c>.
    /// </summary>
    public required string MatchType { get; set; }

    /// <summary><c>in_range</c>, <c>out_of_range</c> or <c>unknown</c>; <c>null</c> for a weak match, which is not checked.</summary>
    public string? VersionVerdict { get; set; }

    /// <summary>The rule that gave the verdict, e.g. <c>in_affected_range</c>, <c>above_all_ranges</c>, <c>distribution_package</c>.</summary>
    public string? VersionReason { get; set; }

    /// <summary>The fix version of the range the installed version lies in (master plan 7.4).</summary>
    public string? FixedVersion { get; set; }

    /// <summary><c>affected</c>, <c>likely</c>, <c>uncertain</c>, <c>unlikely</c> or <c>not_affected</c> (master plan 4.9).</summary>
    public required string Verdict { get; set; }

    /// <summary><c>rules</c>, <c>model</c> or <c>human</c>.</summary>
    public string VerdictSource { get; set; } = "rules";

    public int Level { get; set; }

    /// <summary>The match and the version check with their evidence: keys, installed version, ranges per source.</summary>
    public required JsonDocument Evidence { get; set; }

    /// <summary>
    /// How the rule set reached the level (master plan 7.6): its version, an exclusion, or the factors with
    /// value, evidence and points, the sum and the bounds that applied. Recomputable from the stored inputs.
    /// </summary>
    public required JsonDocument Factors { get; set; }

    /// <summary><c>open</c>, <c>in_review</c>, <c>remediated</c>, <c>risk_accepted</c> or <c>not_affected</c>.</summary>
    public required string Status { get; set; }

    /// <summary>
    /// Why the system set the status: <c>weak_match</c>, <c>out_of_range</c>, <c>withdrawn</c>, <c>merged</c>,
    /// <c>no_longer_matched</c>, <c>component_removed</c>, <c>project_archived</c>; <c>null</c> when a person set it.
    /// </summary>
    public string? StatusReason { get; set; }

    public Guid? StatusChangedBy { get; set; }

    public DateTimeOffset StatusChangedAt { get; set; }

    public string? StatusNote { get; set; }

    /// <summary>Found by the first reverse search of a project or a newly connected repository (master plan 7.7).</summary>
    public bool IsBackfill { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}
