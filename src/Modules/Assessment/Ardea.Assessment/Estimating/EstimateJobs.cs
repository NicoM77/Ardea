using System.Text.Json;
using Ardea.Assessment.Assessing;
using Ardea.Assessment.Persistence;
using Ardea.Assessment.Rules;
using Ardea.Assets.Contracts;
using Ardea.Iam.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Platform.Ai;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ardea.Assessment.Estimating;

/// <summary>
/// The assessment of one alert by the language model (master plan 3.3, 7.5; task 4.3), a job of its own after
/// <see cref="AssessAlert"/>. Runs in the organisation's tenant context. <see cref="RequestedBy"/> names the person
/// who asked for it; the system's own requests are bound to threshold and quota.
/// </summary>
internal sealed record EstimateAlert(Guid EntryId, Guid? RequestedBy = null) : IJob
{
    public static string Kind => "assess.estimate-alert";

    /// <summary>
    /// One waiting job per alert. Waiting jobs are claimed highest level first (master plan 7.5): a person's request
    /// before all, then level 5, 4 and 3. The values rank jobs of this kind only; the queue claims per kind.
    /// </summary>
    public static EnqueueOptions Options(Guid organizationId, Guid entryId, int level, bool person) => new()
    {
        OrganizationId = organizationId,
        CoalesceKey = $"assess.estimate-alert:{organizationId}:{entryId}",
        Priority = person ? JobPriority.Urgent : (JobPriority)(short)(6 - Math.Clamp(level, 1, 5)),
    };
}

/// <summary>
/// When the model is asked (master plan 7.5 «Wann es läuft»): a finding of the alert at level 4 or 5 by the rules, or
/// level 3 with an unknown version; the quota of every affected action free (five assessments the system asked for
/// in seven days); findings of a first fill do not count. Without a provider the organisation may use, never.
/// </summary>
internal sealed class EstimateTrigger(AssessDbContext db, IAiProviders providers, IOrganizationProfiles organizations, IJobQueue jobs, ITenantContext tenant, TimeProvider time)
{
    public const int ActionQuota = 5;
    public static readonly TimeSpan QuotaWindow = TimeSpan.FromDays(7);

    /// <summary>After the rules computed an alert: queues its assessment when threshold and quota allow it.</summary>
    public async Task QueueAfterRulesAsync(Guid entryId, CancellationToken cancellationToken)
    {
        if (providers.All.Count == 0)
        {
            return;
        }

        var alert = await db.Alerts.AsNoTracking().SingleOrDefaultAsync(a => a.EntryId == entryId, cancellationToken);
        if (alert is null)
        {
            return;
        }

        var findings = await db.Findings.AsNoTracking().Where(f => f.AlertId == alert.Id).ToListAsync(cancellationToken);
        if (ThresholdLevel(findings) is not { } level || !await QuotaFreeAsync(findings, cancellationToken) || (await AllowedAsync(cancellationToken)).Count == 0)
        {
            return;
        }

        await jobs.EnqueueAsync(new EstimateAlert(entryId), EstimateAlert.Options(tenant.RequiredOrganizationId, entryId, level, person: false), cancellationToken);
    }

    /// <summary>The providers the organisation's data policy allows, local ones first (master plan 7.8).</summary>
    public async Task<IReadOnlyList<AiProviderInfo>> AllowedAsync(CancellationToken cancellationToken) =>
        providers.All.Count == 0 ? [] : AiDataPolicies.Allowed((await organizations.FindAsync(tenant.RequiredOrganizationId, cancellationToken))?.AiPolicy, providers.All);

    /// <summary>
    /// The highest level by the rules alone among the findings that call for the model, or <c>null</c> when none does.
    /// A level the model already lowered counts as the rules gave it.
    /// </summary>
    public static int? ThresholdLevel(IEnumerable<Finding> findings)
    {
        int? highest = null;
        foreach (var finding in EstimateInput.HitFindings(findings).Where(f => !f.IsBackfill))
        {
            var level = finding.Factors.RootElement.TryGetProperty("levelWithoutModel", out var rules) && rules.ValueKind == JsonValueKind.Number
                ? rules.GetInt32()
                : finding.Level;
            if (level >= 4 || (level == 3 && finding.VersionVerdict == "unknown"))
            {
                highest = Math.Max(highest ?? 0, level);
            }
        }

        return highest;
    }

    /// <summary>Whether every action of the alert's hits has had fewer than five assessments the system asked for in seven days.</summary>
    public async Task<bool> QuotaFreeAsync(IEnumerable<Finding> findings, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow() - QuotaWindow;
        foreach (var action in EstimateInput.HitFindings(findings).Select(f => f.RemediationId).OfType<Guid>().Distinct())
        {
            var used = await db.Assessments.CountAsync(a => a.RequestedBy == null && a.CreatedAt >= since && a.RemediationIds.Contains(action), cancellationToken);
            if (used >= ActionQuota)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>What the read step of <see cref="EstimateAlertHandler"/> decided: the input and providers, or why nothing is asked.</summary>
internal sealed record EstimatePlan(string? Skip, EstimateInput? Input = null, IReadOnlyList<AiProviderInfo>? Providers = null, Guid AlertId = default, int EntryRevision = 0, Guid[]? Actions = null)
{
    public static EstimatePlan Skipped(string reason) => new(reason);
}

/// <summary>The read step: everything the call needs, in a short unit of work of its own.</summary>
internal sealed class EstimatePlanner(
    AssessDbContext db, IEntryQueries entries, IProfileQueries profiles, IOrganizationProfiles organizations, EstimateTrigger trigger,
    RuleSet rules, AiLimits limits, ITenantContext tenant, TimeProvider time)
{
    public async Task<EstimatePlan> PlanAsync(EstimateAlert job, CancellationToken cancellationToken)
    {
        var allowed = await trigger.AllowedAsync(cancellationToken);
        if (allowed.Count == 0)
        {
            return EstimatePlan.Skipped("policy");
        }

        var alert = await db.Alerts.AsNoTracking().SingleOrDefaultAsync(a => a.EntryId == job.EntryId, cancellationToken);
        var entry = alert is null ? null : await entries.FindAsync(job.EntryId, cancellationToken);
        if (alert is null || entry is null || entry.Kind != "vulnerability" || entry.State is not ("published" or "disputed"))
        {
            return EstimatePlan.Skipped("no_alert");
        }

        var findings = await db.Findings.AsNoTracking().Where(f => f.AlertId == alert.Id).ToListAsync(cancellationToken);
        var hits = EstimateInput.HitFindings(findings);
        if (hits.Count == 0)
        {
            return EstimatePlan.Skipped("no_hits");
        }

        if (job.RequestedBy is null && (EstimateTrigger.ThresholdLevel(findings) is null || !await trigger.QuotaFreeAsync(findings, cancellationToken)))
        {
            return EstimatePlan.Skipped("threshold_or_action_quota");
        }

        var organization = await organizations.FindAsync(tenant.RequiredOrganizationId, cancellationToken);
        var components = (await profiles.FindComponentsAsync([.. hits.Select(f => f.ComponentId)], cancellationToken)).ToDictionary(c => c.Id);
        var text = (await entries.TextsAsync([entry.Id], cancellationToken)).FirstOrDefault();
        var input = EstimateInput.Build(entry, text, hits, components, text?.DescriptionSource ?? "source record", organization?.Locale ?? "en", rules.Version);

        // The same input asks nothing new (master plan 4.9).
        if (alert.CurrentAssessmentId is { } current
            && await db.Assessments.AnyAsync(a => a.Id == current && a.Engine == AlertAssessment.ModelEngine && a.InputFingerprint == input.Fingerprint, cancellationToken))
        {
            return EstimatePlan.Skipped("current");
        }

        // The daily token quotas: once used up, the rules alone apply (master plan 7.8).
        var day = AiLimits.DayStart(time.GetUtcNow());
        var organizationTokens = await db.Assessments.Where(a => a.CreatedAt >= day).SumAsync(a => (long)a.TokensIn + a.TokensOut, cancellationToken);
        if (organizationTokens >= limits.DailyTokensPerOrganization)
        {
            return EstimatePlan.Skipped("quota_organization");
        }

        var instanceTokens = await db.Database.SqlQuery<long>($"""SELECT assess.model_tokens_since({day}) AS "Value" """).SingleAsync(cancellationToken);
        if (instanceTokens >= limits.DailyTokens)
        {
            return EstimatePlan.Skipped("quota_instance");
        }

        return new EstimatePlan(null, input, allowed, alert.Id, entry.Revision, [.. hits.Select(f => f.RemediationId).OfType<Guid>().Distinct()]);
    }
}

/// <summary>
/// Read, call, write (ADR 023): the read step in its own short unit of work, the call without an open transaction,
/// the result in the job's unit of work. A checked answer is stored and the alert assessed again with its verdicts;
/// after two failures the attempt is stored with the note «ohne KI-Einschätzung» and the rules stand.
/// </summary>
internal sealed partial class EstimateAlertHandler(
    IUnitOfWorkFactory units, Estimator estimator, AssessDbContext db, IEntryQueries entries, AlertAssessor assessor, Remediations remediations,
    RuleSet rules, TimeProvider time, ILogger<EstimateAlertHandler> logger)
    : IJobHandler<EstimateAlert>
{
    public async Task HandleAsync(EstimateAlert job, JobContext context, CancellationToken cancellationToken)
    {
        EstimatePlan plan;
        await using (var scope = units.CreateScope())
        {
            plan = await scope.ServiceProvider.GetRequiredService<EstimatePlanner>().PlanAsync(job, cancellationToken);
        }

        if (plan.Input is not { } input)
        {
            EstimateMetrics.Estimates.Add(1, new KeyValuePair<string, object?>("outcome", "skipped_" + plan.Skip));
            return;
        }

        var result = await estimator.EstimateAsync(input, plan.Providers!, cancellationToken);
        if (result.Attempts == 0)
        {
            EstimateMetrics.Estimates.Add(1, new KeyValuePair<string, object?>("outcome", "skipped_no_client"));
            return;
        }

        var alert = await db.Alerts.SingleOrDefaultAsync(a => a.Id == plan.AlertId, cancellationToken);
        if (alert is null)
        {
            return;
        }

        var row = new AlertAssessment
        {
            Id = Guid.CreateVersion7(),
            AlertId = alert.Id,
            EntryRevision = plan.EntryRevision,
            InputFingerprint = input.Fingerprint,
            Engine = result.Estimate is null ? AlertAssessment.RulesEngine : AlertAssessment.ModelEngine,
            RulesetVersion = rules.Version,
            Provider = result.Provider?.Name,
            Model = result.Provider?.Model,
            PromptVersion = EstimatePrompt.Version,
            Language = result.Estimate is null ? null : input.Language,
            Output = result.Estimate?.ToDocument() ?? new FailedEstimate(FailedEstimate.WithoutModel, result.Failure!, result.Attempts).ToDocument(),
            TokensIn = result.TokensIn,
            TokensOut = result.TokensOut,
            DurationMs = (int)Math.Min(result.DurationMs, int.MaxValue),
            RequestedBy = job.RequestedBy,
            RemediationIds = plan.Actions ?? [],
            CreatedAt = time.GetUtcNow(),
        };
        db.Assessments.Add(row);
        alert.CurrentAssessmentId = row.Id;
        alert.ChangedAt = row.CreatedAt;
        await db.SaveChangesAsync(cancellationToken);
        EstimateMetrics.Estimates.Add(1, new KeyValuePair<string, object?>("outcome", result.Estimate is null ? FailedEstimate.WithoutModel : "stored"));
        LogStored(alert.Id, row.Engine, result.Provider?.Name, result.Attempts, row.TokensIn, row.TokensOut, row.DurationMs);

        // The verdicts are an input of the rules: the alert is computed again (master plan 7.1 property 2, 7.6).
        if (await entries.FindAsync(job.EntryId, cancellationToken) is { } entry)
        {
            var touched = await assessor.AssessAsync(entry, AssessmentScope.Organization, cancellationToken);
            await remediations.SyncAsync(touched, actor: null, cancellationToken);
        }
    }

    [LoggerMessage(EventId = 6103, Level = LogLevel.Information,
        Message = "Assessment of alert {AlertId}: {Engine} by provider {Provider} after {Attempts} attempts, {TokensIn} tokens in, {TokensOut} out, {DurationMs} ms")]
    private partial void LogStored(Guid alertId, string engine, string? provider, int attempts, int tokensIn, int tokensOut, int durationMs);
}
