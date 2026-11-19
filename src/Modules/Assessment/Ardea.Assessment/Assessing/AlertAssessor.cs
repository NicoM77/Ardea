using System.Text.Json;
using System.Text.Json.Serialization;
using Ardea.Assessment.Matching;
using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Assessing;

/// <summary>What the assessment looks at: every project of the organisation, or one project (the reverse search).</summary>
internal sealed record AssessmentScope(ProjectProfile? Project)
{
    public static readonly AssessmentScope Organization = new((ProjectProfile?)null);

    public static AssessmentScope ForProject(ProjectProfile project) => new(project);
}

/// <summary>
/// Computes the findings of one entry in the current organisation (master plan 3.3 <c>AssessAlert</c>,
/// 7.2, 7.3): candidates through the component keys, version check per strong candidate, findings
/// written only where something changed. The same input changes nothing. Runs in the tenant context
/// of the job or request; a transaction-level advisory lock serialises work on the same alert.
/// </summary>
internal sealed class AlertAssessor(AssessDbContext db, IProfileQueries profiles, IProductCatalog catalog, IVersionSchemes schemes, ITenantContext tenant, TimeProvider time)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public async Task AssessAsync(EntryFacts entry, AssessmentScope scope, CancellationToken cancellationToken)
    {
        var organization = tenant.RequiredOrganizationId;
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"assess.alert:" + organization + ":" + entry.Id}, 0))", cancellationToken);

        var alert = await db.Alerts.SingleOrDefaultAsync(a => a.EntryId == entry.Id, cancellationToken);
        var existing = alert is null ? [] : await db.Findings
            .Where(f => f.AlertId == alert.Id && (scope.Project == null || f.ProjectId == scope.Project.Id))
            .ToListAsync(cancellationToken);

        var relevant = entry.Kind == "vulnerability" && entry.State is "published" or "disputed";
        var keys = relevant ? await EntryKeys.ForAsync(entry, catalog, cancellationToken) : null;
        var components = await ComponentsAsync(keys, existing, scope, cancellationToken);
        var catalogKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var weak in components.SelectMany(c => c.WeakKeys).Where(k => k.Type == "product").Select(k => k.Value).Distinct(StringComparer.Ordinal))
        {
            if (await catalog.FindByKeyAsync(weak, cancellationToken) is not null)
            {
                catalogKeys.Add(weak);
            }
        }

        var candidates = keys?.Match(components.Where(c => !c.ProjectArchived), catalogKeys) ?? [];
        if (alert is null && candidates.Count == 0)
        {
            return;
        }

        var now = time.GetUtcNow();
        if (alert is null)
        {
            alert = new Alert { Id = Guid.CreateVersion7(), EntryId = entry.Id, Level = 1, AssessedEntryRevision = entry.Revision, CreatedAt = now, ChangedAt = now };
            db.Alerts.Add(alert);
        }

        var byComponent = existing.ToDictionary(f => f.ComponentId);
        var present = components.ToDictionary(c => c.Id);
        foreach (var candidate in candidates)
        {
            var outcome = Evaluate(candidate);
            if (byComponent.Remove(candidate.Component.Id, out var finding))
            {
                Apply(finding, outcome, now);
            }
            else
            {
                db.Findings.Add(Create(alert, entry, candidate, outcome, now));
            }
        }

        // What no longer matches closes, with the reason (master plan 6.7, 7.4, 4.9).
        foreach (var finding in byComponent.Values)
        {
            var reason = !relevant ? (entry.State == "merged" ? "merged" : "withdrawn")
                : !present.TryGetValue(finding.ComponentId, out var component) ? "component_removed"
                : component.ProjectArchived ? "project_archived"
                : "no_longer_matched";
            Close(finding, reason, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        var level = await db.Findings.Where(f => f.AlertId == alert.Id).MaxAsync(f => (int?)f.Level, cancellationToken) ?? 1;
        if (alert.Level != level || alert.AssessedEntryRevision != entry.Revision)
        {
            alert.Level = level;
            alert.AssessedEntryRevision = entry.Revision;
            alert.ChangedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The components to look at: in a project's reverse search the project's own; otherwise those of
    /// the organisation that carry one of the entry's keys. Both add the components of existing findings,
    /// so a finding whose component left the profile is seen and closed.
    /// </summary>
    private async Task<IReadOnlyList<ProfileComponent>> ComponentsAsync(EntryKeys? keys, IReadOnlyList<Finding> existing, AssessmentScope scope, CancellationToken cancellationToken)
    {
        if (scope.Project is { } project)
        {
            return project.Archived ? [.. project.Components.Select(c => c with { ProjectArchived = true })] : project.Components;
        }

        var ids = existing.Select(f => f.ComponentId).ToHashSet();
        if (keys is not null)
        {
            ids.UnionWith((await profiles.FindByKeysAsync(keys.All, cancellationToken)).Select(h => h.ComponentId));
        }

        return await profiles.FindComponentsAsync(ids, cancellationToken);
    }

    private Outcome Evaluate(Candidate candidate)
    {
        var evidence = new Dictionary<string, object?>
        {
            ["match"] = new { type = candidate.MatchType, keys = candidate.Keys.Select(k => $"{k.Type} {k.Value}").ToList() },
            ["installed"] = candidate.Component.Version,
        };
        if (!candidate.IsStrong)
        {
            return new Outcome(candidate.MatchType, null, null, null, "not_affected", 1, "weak_match", Document(evidence));
        }

        var version = VersionMatch.Check(candidate.Component, candidate.MatchType, candidate.Affected, schemes);
        evidence["version"] = version;
        var document = Document(evidence);
        return version.Verdict switch
        {
            VersionVerdicts.OutOfRange => new Outcome(candidate.MatchType, version.Verdict, version.Reason, null, "not_affected", 1, "out_of_range", document),
            VersionVerdicts.InRange => new Outcome(candidate.MatchType, version.Verdict, version.Reason, version.FixedVersion, "affected", 2, null, document),
            _ => new Outcome(candidate.MatchType, version.Verdict, version.Reason, null, "uncertain", 2, null, document),
        };
    }

    private static Finding Create(Alert alert, EntryFacts entry, Candidate candidate, Outcome outcome, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        AlertId = alert.Id,
        EntryId = entry.Id,
        ProjectId = candidate.Component.ProjectId,
        ComponentId = candidate.Component.Id,
        MatchType = outcome.MatchType,
        VersionVerdict = outcome.VersionVerdict,
        VersionReason = outcome.VersionReason,
        FixedVersion = outcome.FixedVersion,
        Verdict = outcome.Verdict,
        Level = outcome.Level,
        Evidence = outcome.Evidence,
        Status = outcome.Level >= 2 ? "open" : "not_affected",
        StatusReason = outcome.Level >= 2 ? null : outcome.ExclusionReason,
        StatusChangedAt = now,
        CreatedAt = now,
        ChangedAt = now,
    };

    /// <summary>Writes a new result into an existing finding, only where it differs.</summary>
    private static void Apply(Finding finding, Outcome outcome, DateTimeOffset now)
    {
        var installedBefore = finding.Evidence.RootElement.TryGetProperty("installed", out var before) ? before.GetString() : null;
        var installedNow = outcome.Evidence.RootElement.TryGetProperty("installed", out var after) ? after.GetString() : null;
        var changed = finding.MatchType != outcome.MatchType || finding.VersionVerdict != outcome.VersionVerdict
            || finding.VersionReason != outcome.VersionReason || finding.FixedVersion != outcome.FixedVersion
            || finding.Verdict != outcome.Verdict || finding.Level != outcome.Level
            || !JsonElement.DeepEquals(finding.Evidence.RootElement, outcome.Evidence.RootElement);
        if (changed)
        {
            finding.MatchType = outcome.MatchType;
            finding.VersionVerdict = outcome.VersionVerdict;
            finding.VersionReason = outcome.VersionReason;
            finding.FixedVersion = outcome.FixedVersion;
            finding.Verdict = outcome.Verdict;
            finding.Level = outcome.Level;
            finding.Evidence = outcome.Evidence;
            finding.ChangedAt = now;
        }

        // A status a person set stays; the system's own follows the result (master plan 4.9, 6.7).
        if (finding.StatusChangedBy is not null)
        {
            return;
        }

        var open = finding.Status is "open" or "in_review";
        var (status, reason) = outcome.Level >= 2
            ? (open ? finding.Status : "open", (string?)null)
            // A raised version in the profile closes what it fixes, «fixed by update» (master plan 7.4).
            : open && outcome.ExclusionReason == "out_of_range" && installedBefore != installedNow ? ("remediated", "fixed_by_update")
            : finding.Status == "remediated" && finding.StatusReason == "fixed_by_update" && outcome.ExclusionReason == "out_of_range" ? ("remediated", "fixed_by_update")
            : ("not_affected", outcome.ExclusionReason);
        SetStatus(finding, status, reason, now);
    }

    private static void Close(Finding finding, string reason, DateTimeOffset now)
    {
        if (finding.Level != 1)
        {
            finding.Level = 1;
            finding.ChangedAt = now;
        }

        if (finding.StatusChangedBy is null)
        {
            SetStatus(finding, reason is "component_removed" or "project_archived" ? "remediated" : "not_affected", reason, now);
        }
    }

    private static void SetStatus(Finding finding, string status, string? reason, DateTimeOffset now)
    {
        if (finding.Status == status && finding.StatusReason == reason)
        {
            return;
        }

        finding.Status = status;
        finding.StatusReason = reason;
        finding.StatusChangedAt = now;
        finding.ChangedAt = now;
    }

    private static JsonDocument Document(object value) => JsonSerializer.SerializeToDocument(value, Json);

    /// <summary>The result for one finding.</summary>
    private sealed record Outcome(
        string MatchType, string? VersionVerdict, string? VersionReason, string? FixedVersion, string Verdict, int Level, string? ExclusionReason, JsonDocument Evidence);
}
