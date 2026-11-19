using System.Text.Json;
using System.Text.Json.Serialization;
using Ardea.Assessment.Matching;
using Ardea.Assessment.Persistence;
using Ardea.Assessment.Rules;
using Ardea.Assets.Contracts;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Platform.Jobs;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Assessing;

/// <summary>
/// What the assessment looks at: every project of the organisation, or one project (the reverse search).
/// <see cref="Backfill"/> names the components whose new findings belong to a first fill (master plan 7.7).
/// </summary>
internal sealed record AssessmentScope(ProjectProfile? Project, IReadOnlySet<Guid> Backfill)
{
    public static readonly AssessmentScope Organization = new(null, new HashSet<Guid>());

    public static AssessmentScope ForProject(ProjectProfile project, IReadOnlySet<Guid>? backfill = null) => new(project, backfill ?? new HashSet<Guid>());
}

/// <summary>
/// Computes the findings of one entry in the current organisation (master plan 3.3 <c>AssessAlert</c>,
/// 7.2, 7.3, 7.6): candidates through the component keys, version check per strong candidate, the level
/// from the rule set, findings written only where something changed. The same input changes nothing.
/// Runs in the tenant context of the job or request; a transaction-level advisory lock serialises work
/// on the same alert. The caller brings the actions in step afterwards (<see cref="Remediations"/>).
/// </summary>
internal sealed class AlertAssessor(
    AssessDbContext db,
    IProfileQueries profiles,
    IProductCatalog catalog,
    IVersionSchemes schemes,
    RuleSet rules,
    Numbers numbers,
    History history,
    IJobQueue jobs,
    ITenantContext tenant,
    TimeProvider time)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <returns>The components whose findings changed.</returns>
    public async Task<IReadOnlyCollection<Guid>> AssessAsync(EntryFacts entry, AssessmentScope scope, CancellationToken cancellationToken)
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
        if (scope.Project is null)
        {
            candidates = await WithoutFirstFillAsync(candidates, existing, cancellationToken);
        }

        if (alert is null && candidates.Count == 0)
        {
            return [];
        }

        var now = time.GetUtcNow();
        if (alert is null)
        {
            alert = new Alert { Id = Guid.CreateVersion7(), EntryId = entry.Id, EntryKey = entry.Key, Level = 1, AssessedEntryRevision = entry.Revision, CreatedAt = now, ChangedAt = now };
            db.Alerts.Add(alert);
        }

        var byComponent = existing.ToDictionary(f => f.ComponentId);
        var present = components.ToDictionary(c => c.Id);
        foreach (var candidate in candidates)
        {
            byComponent.TryGetValue(candidate.Component.Id, out var known);
            var outcome = Evaluate(entry, candidate, known);
            if (byComponent.Remove(candidate.Component.Id, out var finding))
            {
                Apply(finding, outcome, now);
            }
            else
            {
                db.Findings.Add(Create(alert, entry, candidate, outcome, scope.Backfill.Contains(candidate.Component.Id), now));
            }
        }

        // What no longer matches closes, with the reason (master plan 6.7, 7.4, 4.9).
        foreach (var finding in byComponent.Values)
        {
            var reason = !relevant ? (entry.State == "merged" ? "merged" : "withdrawn")
                : !present.TryGetValue(finding.ComponentId, out var component) ? "component_removed"
                : component.ProjectArchived ? "project_archived"
                : "no_longer_matched";
            Close(finding, reason, Document(new RuleResult { RulesetVersion = rules.Version, Level = 1, Exclusion = reason }), now);
        }

        var touched = db.ChangeTracker.Entries<Finding>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity.ComponentId)
            .ToHashSet();
        await db.SaveChangesAsync(cancellationToken);

        var findings = await db.Findings.Where(f => f.AlertId == alert.Id).Select(f => new { f.Level, f.Status }).ToListAsync(cancellationToken);
        var level = findings.Count == 0 ? 1 : findings.Max(f => f.Level);
        if (alert.Level != level || alert.AssessedEntryRevision != entry.Revision || alert.EntryKey != entry.Key)
        {
            alert.Level = level;
            alert.AssessedEntryRevision = entry.Revision;
            alert.EntryKey = entry.Key;
            alert.ChangedAt = now;
        }

        // The alert gets its number once one of its findings reaches level 2 (master plan 4.6).
        if (alert.Number is null && findings.Exists(f => f.Status is "open" or "in_review"))
        {
            alert.Number = await numbers.NextAsync(cancellationToken);
            alert.FirstRaisedAt = now;
            alert.ChangedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return touched;
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

    /// <summary>
    /// A project's first look, and a newly connected repository's, belong to its first fill (master plan
    /// 7.7): the forward search leaves such components to the reverse search, which marks their findings
    /// and sends a single <c>backfill.completed</c>. It queues that search, should none be waiting.
    /// </summary>
    private async Task<IReadOnlyList<Candidate>> WithoutFirstFillAsync(IReadOnlyList<Candidate> candidates, IReadOnlyList<Finding> existing, CancellationToken cancellationToken)
    {
        var known = existing.Select(f => f.ComponentId).ToHashSet();
        var projects = candidates.Where(c => !known.Contains(c.Component.Id)).Select(c => c.Component.ProjectId).Distinct().ToArray();
        if (projects.Length == 0)
        {
            return candidates;
        }

        var seen = await db.MatchScopes.AsNoTracking().Where(s => projects.Contains(s.ProjectId)).Select(s => new { s.ProjectId, s.RepositoryLinkId }).ToListAsync(cancellationToken);
        var kept = new List<Candidate>(candidates.Count);
        var unseen = new SortedSet<Guid>();
        foreach (var candidate in candidates)
        {
            var c = candidate.Component;
            var firstFill = !known.Contains(c.Id)
                && (!seen.Exists(s => s.ProjectId == c.ProjectId && s.RepositoryLinkId == null)
                    || (c.RepositoryLinkId is { } link && !seen.Exists(s => s.ProjectId == c.ProjectId && s.RepositoryLinkId == link)));
            if (firstFill)
            {
                unseen.Add(c.ProjectId);
            }
            else
            {
                kept.Add(candidate);
            }
        }

        foreach (var project in unseen)
        {
            await jobs.EnqueueAsync(new MatchProject(project), MatchProject.Options(tenant.RequiredOrganizationId, project), cancellationToken);
        }

        return kept;
    }

    /// <summary>Match, version check and rule set for one candidate (master plan 7.2, 7.3, 7.6).</summary>
    private Outcome Evaluate(EntryFacts entry, Candidate candidate, Finding? known)
    {
        var evidence = new Dictionary<string, object?>
        {
            ["match"] = new { type = candidate.MatchType, keys = candidate.Keys.Select(k => $"{k.Type} {k.Value}").ToList() },
            ["installed"] = candidate.Component.Version,
        };
        var version = candidate.IsStrong ? VersionMatch.Check(candidate.Component, candidate.MatchType, candidate.Affected, schemes) : null;
        if (version is not null)
        {
            evidence["version"] = version;
        }

        var component = candidate.Component;
        var human = known is { VerdictSource: "human" } ? known.Verdict : null;
        var input = new RuleInput
        {
            StrongMatch = candidate.IsStrong,
            VersionVerdict = version?.Verdict,
            VersionReason = version?.Reason,
            InstalledVersion = component.Version,
            HumanExcluded = known is { StatusChangedBy: not null, Status: "not_affected" },
            HumanVerdict = human is "affected" or "likely" or "unlikely" ? human : null,
            CvssScore = entry.CvssScore,
            CvssVersion = entry.CvssVersion,
            Exploitation = entry.Exploitation,
            ExploitationSource = entry.ExploitationSource,
            ExploitationSince = entry.ExploitationSince,
            AuthorityWarning = entry.AuthorityWarning,
            EpssClass = entry.EpssClass,
            Criticality = component.Criticality,
            Exposure = component.Exposure,
            Protection = component.Protection,
            DevelopmentOnly = component.DependencyScope == "development",
        };
        var result = RuleEngine.Evaluate(rules, input);

        // What the rules say without a person's «not affected»: whether a reassessment reopens it (master plan 6.7).
        var withoutPerson = input.HumanExcluded ? RuleEngine.Evaluate(rules, input with { HumanExcluded = false }) : result;

        // The verdict without a model (master plan 4.9).
        var verdict = version?.Verdict switch
        {
            VersionVerdicts.InRange => "affected",
            VersionVerdicts.Unknown => "uncertain",
            _ => "not_affected",
        };
        return new Outcome(
            candidate.MatchType, version?.Verdict, version?.Reason, version?.Verdict == VersionVerdicts.InRange ? version.FixedVersion : null,
            verdict, result.Level, result.Exclusion, Document(evidence), Document(result),
            entry.Exploitation, entry.FixAvailable, withoutPerson.Level, Document(withoutPerson));
    }

    private static Finding Create(Alert alert, EntryFacts entry, Candidate candidate, Outcome outcome, bool backfill, DateTimeOffset now) => new()
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
        Factors = outcome.Factors,
        Exploitation = outcome.Exploitation,
        FixAvailable = outcome.FixAvailable,
        Status = outcome.Level >= 2 ? "open" : "not_affected",
        StatusReason = outcome.Level >= 2 ? null : outcome.ExclusionReason,
        StatusChangedAt = now,
        IsBackfill = backfill,
        CreatedAt = now,
        ChangedAt = now,
    };

    /// <summary>Writes a new result into an existing finding, only where it differs.</summary>
    private void Apply(Finding finding, Outcome outcome, DateTimeOffset now)
    {
        var installedBefore = finding.Evidence.RootElement.TryGetProperty("installed", out var before) ? before.GetString() : null;
        var installedNow = outcome.Evidence.RootElement.TryGetProperty("installed", out var after) ? after.GetString() : null;
        var personClosed = finding.StatusChangedBy is not null && finding.Status is "remediated" or "risk_accepted" or "not_affected";

        // A finding a person closed opens again only when its level rises or the version check says «in range» again (master plan 6.7).
        string? reopen = !personClosed ? null
            : outcome.ReopenLevel > (finding.ClosedLevel ?? finding.Level) && outcome.ReopenLevel >= 2 ? "level_raised"
            : outcome.VersionVerdict == VersionVerdicts.InRange && finding.ClosedVersionVerdict != VersionVerdicts.InRange && outcome.ReopenLevel >= 2 ? "in_range_again"
            : null;
        var (level, factors) = reopen is null ? (outcome.Level, outcome.Factors) : (outcome.ReopenLevel, outcome.ReopenFactors);

        var changed = finding.MatchType != outcome.MatchType || finding.VersionVerdict != outcome.VersionVerdict
            || finding.VersionReason != outcome.VersionReason || finding.FixedVersion != outcome.FixedVersion
            || (finding.VerdictSource == "rules" && finding.Verdict != outcome.Verdict) || finding.Level != level
            || finding.Exploitation != outcome.Exploitation || finding.FixAvailable != outcome.FixAvailable
            || !JsonElement.DeepEquals(finding.Evidence.RootElement, outcome.Evidence.RootElement)
            || !JsonElement.DeepEquals(finding.Factors.RootElement, factors.RootElement);
        if (changed)
        {
            finding.MatchType = outcome.MatchType;
            finding.VersionVerdict = outcome.VersionVerdict;
            finding.VersionReason = outcome.VersionReason;
            finding.FixedVersion = outcome.FixedVersion;
            finding.Verdict = finding.VerdictSource == "rules" ? outcome.Verdict : finding.Verdict;
            finding.Level = level;
            finding.Evidence = outcome.Evidence;
            finding.Factors = factors;
            finding.Exploitation = outcome.Exploitation;
            finding.FixAvailable = outcome.FixAvailable;
            finding.ChangedAt = now;
        }

        if (personClosed)
        {
            if (reopen is not null)
            {
                SetStatus(finding, "open", null, now);
                history.Add(EventKinds.FindingReopened, new { reason = reopen, finding.Level }, finding.RemediationId, finding.Id);
            }

            return;
        }

        var open = finding.Status is "open" or "in_review";
        var (status, reason) = outcome.Level >= 2
            ? (open ? finding.Status : "open", (string?)null)
            // A raised version in the profile closes what it fixes, «fixed by update» (master plan 7.4).
            : open && outcome.ExclusionReason == "out_of_range" && installedBefore != installedNow ? ("remediated", "fixed_by_update")
            : finding.Status == "remediated" && finding.StatusReason == "fixed_by_update" && outcome.ExclusionReason == "out_of_range" ? ("remediated", "fixed_by_update")
            : ("not_affected", outcome.ExclusionReason);
        if (!open && status == "open" && finding.RemediationId is not null)
        {
            history.Add(EventKinds.FindingReopened, new { reason = "reassessed", finding.Level }, finding.RemediationId, finding.Id);
        }

        SetStatus(finding, status, reason, now);
    }

    private void Close(Finding finding, string reason, JsonDocument factors, DateTimeOffset now)
    {
        if (finding.Level != 1 || !JsonElement.DeepEquals(finding.Factors.RootElement, factors.RootElement))
        {
            finding.Level = 1;
            finding.Factors = factors;
            finding.ChangedAt = now;
        }

        if (finding.StatusChangedBy is null || finding.Status is "open" or "in_review")
        {
            SetStatus(finding, reason is "component_removed" or "project_archived" ? "remediated" : "not_affected", reason, now);
        }
    }

    /// <summary>Sets a status of the system; closing an open finding is part of its action's history.</summary>
    private void SetStatus(Finding finding, string status, string? reason, DateTimeOffset now)
    {
        if (finding.Status == status && finding.StatusReason == reason)
        {
            return;
        }

        if (finding.Status is "open" or "in_review" && status is not ("open" or "in_review") && finding.RemediationId is not null)
        {
            history.Add(EventKinds.FindingClosed, new { reason = reason ?? status, status }, finding.RemediationId, finding.Id);
        }

        finding.Status = status;
        finding.StatusReason = reason;
        finding.StatusChangedBy = null;
        finding.StatusChangedAt = now;
        finding.ChangedAt = now;
    }

    private static JsonDocument Document(object value) => JsonSerializer.SerializeToDocument(value, Json);

    /// <summary>The result for one finding.</summary>
    private sealed record Outcome(
        string MatchType, string? VersionVerdict, string? VersionReason, string? FixedVersion, string Verdict, int Level, string? ExclusionReason,
        JsonDocument Evidence, JsonDocument Factors, string Exploitation, bool FixAvailable, int ReopenLevel, JsonDocument ReopenFactors);
}
