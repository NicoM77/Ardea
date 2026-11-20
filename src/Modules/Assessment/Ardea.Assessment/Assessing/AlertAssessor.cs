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

internal sealed record AssessmentScope(ProjectProfile? Project, IReadOnlySet<Guid> Backfill, IReadOnlySet<Guid>? Components = null)
{
    public static readonly AssessmentScope Organization = new(null, new HashSet<Guid>());

    public static AssessmentScope ForProject(ProjectProfile project, IReadOnlySet<Guid>? backfill = null) => new(project, backfill ?? new HashSet<Guid>());

    public static AssessmentScope ForComponent(Guid componentId) => ForComponents(new HashSet<Guid> { componentId });

    public static AssessmentScope ForComponents(IReadOnlySet<Guid> componentIds) => new(null, new HashSet<Guid>(), componentIds);
}

internal sealed class AlertAssessor(
    AssessDbContext db,
    IProfileQueries profiles,
    IProductCatalog catalog,
    IVersionSchemes schemes,
    RuleSet rules,
    Numbers numbers,
    History history,
    Notices notices,
    IJobQueue jobs,
    ITenantContext tenant,
    TimeProvider time)
{
    internal const string MovedToNotice = "moved_to_notice";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public async Task<IReadOnlyCollection<Guid>> AssessAsync(EntryFacts entry, AssessmentScope scope, CancellationToken cancellationToken)
    {
        await LockAsync(entry.Id, cancellationToken);

        var alert = await db.Alerts.SingleOrDefaultAsync(a => a.EntryId == entry.Id, cancellationToken);
        var only = scope.Components?.ToArray();
        var existing = alert is null ? [] : await db.Findings
            .Where(f => f.AlertId == alert.Id && (scope.Project == null || f.ProjectId == scope.Project.Id) && (only == null || only.Contains(f.ComponentId)))
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
        if (scope.Project is null && scope.Components is null)
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

        var modelVerdicts = await ModelVerdictsAsync(alert, entry, cancellationToken);
        var learnedProducts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in candidates.SelectMany(c => c.Keys.Where(k => k.Type == "product").Select(k => k.Value).Append(c.Component.ProductKey)).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            if (await catalog.FindByKeyAsync(key, cancellationToken) is { Origin: ProductOrigin.Learned })
            {
                learnedProducts.Add(key);
            }
        }

        var byComponent = existing.ToDictionary(f => f.ComponentId);
        var present = components.ToDictionary(c => c.Id);
        var hit = new HashSet<Guid>();
        var raising = new HashSet<Guid>();
        var handed = new HashSet<Guid>();
        foreach (var candidate in candidates)
        {
            byComponent.TryGetValue(candidate.Component.Id, out var known);
            var coarse = candidate.IsStrong && NoticeRules.IsCoarse(candidate.Component);

            var handedOver = coarse && known is { StatusChangedBy: null } && (known.Status == "open" || known.VerdictSource == "human");
            if (handedOver && known!.StatusReason != MovedToNotice)
            {
                handed.Add(candidate.Component.Id);
            }

            var outcome = Evaluate(entry, candidate, known, modelVerdicts.GetValueOrDefault(candidate.Component.Id), keys!.Learned(candidate, learnedProducts), coarse);
            if (byComponent.Remove(candidate.Component.Id, out var finding))
            {
                Apply(finding, outcome, coarse, now);
            }
            else
            {
                db.Findings.Add(Create(alert, entry, candidate, outcome, scope.Backfill.Contains(candidate.Component.Id), coarse, now));
            }

            if (handedOver || (coarse && LineNarrowing.Keeps(outcome.Statements, NoticeRules.Line(candidate.Component))))
            {
                hit.Add(candidate.Component.Id);
                var noisy = candidate.Component.ProductKey is { } product && (await catalog.FindByKeyAsync(product, cancellationToken))?.Noisy == true;
                if (handedOver || NoticeRules.Meets(candidate.Component.NoticeThreshold, noisy, entry))
                {
                    raising.Add(candidate.Component.Id);
                }
            }
        }

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

        var looked = components.Select(c => c.Id).Concat(existing.Select(f => f.ComponentId)).ToHashSet();
        await notices.UpdateAsync(entry, new CoarseHits(looked, hit, raising, handed, Quiet: scope.Project is not null), cancellationToken);

        var findings = await db.Findings.Where(f => f.AlertId == alert.Id).Select(f => new { f.Level, f.Status }).ToListAsync(cancellationToken);
        var level = findings.Count == 0 ? 1 : findings.Max(f => f.Level);
        if (alert.Level != level || alert.AssessedEntryRevision != entry.Revision || alert.EntryKey != entry.Key)
        {
            alert.Level = level;
            alert.AssessedEntryRevision = entry.Revision;
            alert.EntryKey = entry.Key;
            alert.ChangedAt = now;
        }

        if (alert.Number is null && findings.Exists(f => f.Status == "open"))
        {
            alert.Number = await numbers.NextAsync(cancellationToken);
            alert.FirstRaisedAt = now;
            alert.ChangedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return touched;
    }

    public async Task LockAsync(Guid entryId, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"assess.alert:" + tenant.RequiredOrganizationId + ":" + entryId}, 0))", cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, Estimating.StoredVerdict>> ModelVerdictsAsync(Alert alert, EntryFacts entry, CancellationToken cancellationToken)
    {
        if (alert.CurrentAssessmentId is not { } id)
        {
            return new Dictionary<Guid, Estimating.StoredVerdict>();
        }

        var current = await db.Assessments.AsNoTracking()
            .Where(a => a.Id == id && a.Engine == AlertAssessment.ModelEngine && a.EntryRevision == entry.Revision)
            .Select(a => a.Output).SingleOrDefaultAsync(cancellationToken);
        return current is null ? new Dictionary<Guid, Estimating.StoredVerdict>() : Estimating.StoredEstimate.VerdictsOf(current);
    }

    private async Task<IReadOnlyList<ProfileComponent>> ComponentsAsync(EntryKeys? keys, IReadOnlyList<Finding> existing, AssessmentScope scope, CancellationToken cancellationToken)
    {
        if (scope.Project is { } project)
        {
            return project.Archived ? [.. project.Components.Select(c => c with { ProjectArchived = true })] : project.Components;
        }

        if (scope.Components is { } only)
        {
            return await profiles.FindComponentsAsync([.. only], cancellationToken);
        }

        var ids = existing.Select(f => f.ComponentId).ToHashSet();
        if (keys is not null)
        {
            ids.UnionWith((await profiles.FindByKeysAsync(keys.All, cancellationToken)).Select(h => h.ComponentId));
        }

        return await profiles.FindComponentsAsync(ids, cancellationToken);
    }

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

    private Outcome Evaluate(EntryFacts entry, Candidate candidate, Finding? known, Estimating.StoredVerdict? modelVerdict, LearnedHit? learned, bool coarse)
    {
        var evidence = new Dictionary<string, object?>
        {
            ["match"] = new { type = candidate.MatchType, keys = candidate.Keys.Select(k => $"{k.Type} {k.Value}").ToList() },
            ["installed"] = candidate.Component.Version,
        };
        if (learned is not null)
        {
            evidence["learned"] = new { productKey = learned.ProductKey, rawKeys = learned.RawKeys };
        }

        var component = coarse ? candidate.Component with { Version = null } : candidate.Component;
        var version = candidate.IsStrong ? VersionMatch.Check(component, candidate.MatchType, candidate.Affected, schemes, entry.FixVersionsInText) : null;
        if (version is not null)
        {
            evidence["version"] = version;
        }

        var human = known is { VerdictSource: "human" } ? known.Verdict : null;

        var model = version?.Verdict == VersionVerdicts.Unknown && human is null
            && modelVerdict is { } stored && stored.Context == Estimating.EstimateInput.Context(component, version.Verdict)
            ? Estimating.StoredEstimate.Name(stored.Verdict)
            : null;
        var input = new RuleInput
        {
            StrongMatch = candidate.IsStrong,
            VersionVerdict = version?.Verdict,
            VersionReason = version?.Reason,
            InstalledVersion = component.Version,
            HumanExcluded = known is { StatusChangedBy: not null, Status: "not_affected" },
            HumanVerdict = human is "affected" or "likely" or "unlikely" ? human : human == "not_affected" ? "unlikely" : null,
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
            ModelVerdict = model,
        };
        var result = RuleEngine.Evaluate(rules, input);

        var withoutPerson = input.HumanExcluded ? RuleEngine.Evaluate(rules, input with { HumanExcluded = false }) : result;

        var verdict = version?.Verdict switch
        {
            VersionVerdicts.InRange => "affected",
            VersionVerdicts.Unknown => "uncertain",
            _ => "not_affected",
        };
        var source = "rules";
        if (model is not null && result.Exclusion is null)
        {
            (verdict, source) = (model, "model");
        }

        return new Outcome(
            candidate.MatchType, version?.Verdict, version?.Reason, version?.Verdict == VersionVerdicts.InRange ? version.FixedVersion : null,
            verdict, source, result.Level, result.Exclusion, Document(evidence), Document(result),
            entry.Exploitation, entry.FixAvailable, withoutPerson.Level, Document(withoutPerson), version?.Statements ?? []);
    }

    private Finding Create(Alert alert, EntryFacts entry, Candidate candidate, Outcome outcome, bool backfill, bool coarse, DateTimeOffset now)
    {
        var (level, factors, status, reason) = coarse ? (1, Routed(outcome), "not_affected", MovedToNotice)
            : outcome.Level >= 2 ? (outcome.Level, outcome.Factors, "open", null)
            : (outcome.Level, outcome.Factors, "not_affected", outcome.ExclusionReason);
        return new Finding
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
            VerdictSource = outcome.VerdictSource,
            Level = level,
            Evidence = outcome.Evidence,
            Factors = factors,
            Exploitation = outcome.Exploitation,
            FixAvailable = outcome.FixAvailable,
            Status = status,
            StatusReason = reason,
            StatusChangedAt = now,
            IsBackfill = backfill,
            CreatedAt = now,
            ChangedAt = now,
        };
    }

    private JsonDocument Routed(Outcome outcome) =>
        Document(new RuleResult { RulesetVersion = rules.Version, Level = 1, Exclusion = MovedToNotice, PreciseLevel = outcome.Level });

    private void Apply(Finding finding, Outcome outcome, bool coarse, DateTimeOffset now)
    {
        var installedBefore = finding.Evidence.RootElement.TryGetProperty("installed", out var before) ? before.GetString() : null;
        var installedNow = outcome.Evidence.RootElement.TryGetProperty("installed", out var after) ? after.GetString() : null;
        var personClosed = finding.StatusChangedBy is not null && finding.Status is "remediated" or "risk_accepted" or "not_affected";

        var routed = coarse && finding.StatusChangedBy is null;
        var personOpened = coarse && finding is { StatusChangedBy: not null, Status: "open" };

        string? reopen = !personClosed || coarse ? null
            : outcome.ReopenLevel > (finding.ClosedLevel ?? finding.Level) && outcome.ReopenLevel >= 2 ? "level_raised"
            : outcome.VersionVerdict == VersionVerdicts.InRange && finding.ClosedVersionVerdict != VersionVerdicts.InRange && outcome.ReopenLevel >= 2 ? "in_range_again"
            : null;

        var (level, factors) = routed ? (1, Routed(outcome))
            : personOpened ? (Math.Max(outcome.Level, 2), outcome.Factors)
            : reopen is null ? (outcome.Level, outcome.Factors)
            : (outcome.ReopenLevel, outcome.ReopenFactors);

        var changed = finding.MatchType != outcome.MatchType || finding.VersionVerdict != outcome.VersionVerdict
            || finding.VersionReason != outcome.VersionReason || finding.FixedVersion != outcome.FixedVersion
            || (finding.VerdictSource != "human" && (finding.Verdict != outcome.Verdict || finding.VerdictSource != outcome.VerdictSource)) || finding.Level != level
            || finding.Exploitation != outcome.Exploitation || finding.FixAvailable != outcome.FixAvailable
            || !JsonElement.DeepEquals(finding.Evidence.RootElement, outcome.Evidence.RootElement)
            || !JsonElement.DeepEquals(finding.Factors.RootElement, factors.RootElement);
        if (changed)
        {
            finding.MatchType = outcome.MatchType;
            finding.VersionVerdict = outcome.VersionVerdict;
            finding.VersionReason = outcome.VersionReason;
            finding.FixedVersion = outcome.FixedVersion;
            (finding.Verdict, finding.VerdictSource) = finding.VerdictSource == "human" ? (finding.Verdict, finding.VerdictSource) : (outcome.Verdict, outcome.VerdictSource);
            finding.Level = level;
            finding.Evidence = outcome.Evidence;
            finding.Factors = factors;
            finding.Exploitation = outcome.Exploitation;
            finding.FixAvailable = outcome.FixAvailable;
            finding.ChangedAt = now;
        }

        if (routed)
        {
            SetStatus(finding, "not_affected", MovedToNotice, now);
            return;
        }

        if (personOpened)
        {
            return;
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

        var open = finding.Status == "open";
        var (status, reason) = outcome.Level >= 2
            ? (open ? finding.Status : "open", (string?)null)
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

        if (finding.StatusChangedBy is null || finding.Status == "open")
        {
            SetStatus(finding, reason is "component_removed" or "project_archived" ? "remediated" : "not_affected", reason, now);
        }
    }

    private void SetStatus(Finding finding, string status, string? reason, DateTimeOffset now)
    {
        if (finding.Status == status && finding.StatusReason == reason)
        {
            return;
        }

        if (finding.Status == "open" && status != "open" && finding.RemediationId is not null)
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

    private sealed record Outcome(
        string MatchType, string? VersionVerdict, string? VersionReason, string? FixedVersion, string Verdict, string VerdictSource, int Level, string? ExclusionReason,
        JsonDocument Evidence, JsonDocument Factors, string Exploitation, bool FixAvailable, int ReopenLevel, JsonDocument ReopenFactors,
        IReadOnlyList<VersionStatement> Statements);
}
