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

internal sealed record EstimateAlert(Guid EntryId, Guid? RequestedBy = null) : IJob
{
    public static string Kind => "assess.estimate-alert";

    public static EnqueueOptions Options(Guid organizationId, Guid entryId, int level, bool person) => new()
    {
        OrganizationId = organizationId,
        CoalesceKey = $"assess.estimate-alert:{organizationId}:{entryId}" + (person ? ":person" : string.Empty),
        Priority = person ? JobPriority.Urgent : (JobPriority)(short)(6 - Math.Clamp(level, 1, 5)),
    };
}

internal sealed class EstimateTrigger(
    AssessDbContext db, IAiProviders providers, IOrganizationProfiles organizations, IJobQueue jobs, AiLimits limits, ITenantContext tenant, TimeProvider time)
{
    public const int ActionQuota = 5;
    public static readonly TimeSpan QuotaWindow = TimeSpan.FromDays(7);

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
        if (ThresholdLevel(findings) is not { } level || await CallsLeftAsync(findings, cancellationToken) <= 0 || (await AllowedAsync(cancellationToken)).Count == 0)
        {
            return;
        }

        await jobs.EnqueueAsync(new EstimateAlert(entryId), EstimateAlert.Options(tenant.RequiredOrganizationId, entryId, level, person: false), cancellationToken);
    }

    public async Task<IReadOnlyList<AiProviderInfo>> AllowedAsync(CancellationToken cancellationToken) =>
        providers.All.Count == 0 ? [] : AiDataPolicies.Allowed((await organizations.FindAsync(tenant.RequiredOrganizationId, cancellationToken))?.AiPolicy, providers.All);

    public static int? ThresholdLevel(IEnumerable<Finding> findings)
    {
        int? highest = null;
        foreach (var finding in EstimateInput.HitFindings(findings).Where(f => !f.IsBackfill))
        {
            var level = finding.Factors.RootElement.ValueKind == JsonValueKind.Object
                && finding.Factors.RootElement.TryGetProperty("levelWithoutModel", out var rules) && rules.ValueKind == JsonValueKind.Number
                ? rules.GetInt32()
                : finding.Level;
            if (level >= 4 || (level == 3 && finding.VersionVerdict == "unknown"))
            {
                highest = Math.Max(highest ?? 0, level);
            }
        }

        return highest;
    }

    public async Task<int> CallsLeftAsync(IEnumerable<Finding> findings, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow() - QuotaWindow;
        var left = ActionQuota;
        foreach (var action in EstimateInput.HitFindings(findings).Select(f => f.RemediationId).OfType<Guid>().Distinct())
        {
            var used = await db.Assessments.Where(a => a.RequestedBy == null && a.CreatedAt >= since && a.RemediationIds.Contains(action))
                .SumAsync(a => a.Attempts, cancellationToken);
            left = Math.Min(left, ActionQuota - used);
        }

        return left;
    }

    public async Task<bool> PersonRequestsLeftAsync(CancellationToken cancellationToken)
    {
        var day = AiLimits.DayStart(time.GetUtcNow());
        return await db.Assessments.CountAsync(a => a.RequestedBy != null && a.CreatedAt >= day, cancellationToken) < limits.DailyRequestsPerOrganization;
    }
}

internal sealed record EstimatePlan(
    string? Skip, EstimateInput? Input = null, IReadOnlyList<AiProviderInfo>? Providers = null, Guid AlertId = default, int EntryRevision = 0,
    Guid[]? Actions = null, int Budget = 0)
{
    public static EstimatePlan Skipped(string reason) => new(reason);
}

internal sealed class EstimatePlanner(
    AssessDbContext db, IEntryQueries entries, IProfileQueries profiles, IOrganizationProfiles organizations, EstimateTrigger trigger,
    AiLimits limits, ITenantContext tenant, TimeProvider time)
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

        var person = job.RequestedBy is not null;
        var budget = person ? Estimator.Attempts : await trigger.CallsLeftAsync(findings, cancellationToken);
        if (!person && (EstimateTrigger.ThresholdLevel(findings) is null || budget <= 0))
        {
            return EstimatePlan.Skipped("threshold_or_action_quota");
        }

        if (person && !await trigger.PersonRequestsLeftAsync(cancellationToken))
        {
            return EstimatePlan.Skipped("person_requests");
        }

        var organization = await organizations.FindAsync(tenant.RequiredOrganizationId, cancellationToken);
        var components = (await profiles.FindComponentsAsync([.. hits.Select(f => f.ComponentId)], cancellationToken)).ToDictionary(c => c.Id);
        var text = (await entries.TextsAsync([entry.Id], cancellationToken)).FirstOrDefault();
        var input = EstimateInput.Build(entry, text, hits, components, text?.DescriptionSource ?? "source record", organization?.Locale ?? "en");

        if (alert.CurrentAssessmentId is { } current
            && await db.Assessments.AnyAsync(a => a.Id == current && a.InputFingerprint == input.Fingerprint && (!person || a.Engine == AlertAssessment.ModelEngine), cancellationToken))
        {
            return EstimatePlan.Skipped("current");
        }

        var day = AiLimits.DayStart(time.GetUtcNow());
        var organizationTokens = await db.Assessments.Where(a => a.CreatedAt >= day).SumAsync(a => (long)a.TokensIn + a.TokensOut, cancellationToken);
        if (organizationTokens >= limits.DailyTokensPerOrganization)
        {
            return EstimatePlan.Skipped("quota_organization");
        }

        if (await db.Database.SqlQuery<bool>($"""SELECT assess.model_tokens_exhausted({day}, {limits.DailyTokens}) AS "Value" """).SingleAsync(cancellationToken))
        {
            return EstimatePlan.Skipped("quota_instance");
        }

        return new EstimatePlan(null, input, allowed, alert.Id, entry.Revision, [.. hits.Select(f => f.RemediationId).OfType<Guid>().Distinct()], budget);
    }
}

internal sealed partial class EstimateAlertHandler(
    IUnitOfWorkFactory units, Estimator estimator, AssessDbContext db, IJobQueue jobs, RuleSet rules, ITenantContext tenant, TimeProvider time,
    ILogger<EstimateAlertHandler> logger)
    : IJobHandler<EstimateAlert>
{
    public async Task HandleAsync(EstimateAlert job, JobContext context, CancellationToken cancellationToken)
    {
        if (context.Attempt > 1)
        {
            EstimateMetrics.Estimates.Add(1, new KeyValuePair<string, object?>("outcome", "skipped_repeated_job"));
            return;
        }

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

        var result = await estimator.EstimateAsync(input, plan.Providers!, plan.Budget, cancellationToken);
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
            Attempts = result.Attempts,
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

        await jobs.EnqueueAsync(new AssessAlert(job.EntryId), AssessAlert.Options(tenant.RequiredOrganizationId, job.EntryId), cancellationToken);
        EstimateMetrics.Estimates.Add(1, new KeyValuePair<string, object?>("outcome", result.Estimate is null ? FailedEstimate.WithoutModel : "stored"));
        LogStored(alert.Id, row.Engine, result.Provider?.Name, result.Attempts, row.TokensIn, row.TokensOut, row.DurationMs);
    }

    [LoggerMessage(EventId = 6103, Level = LogLevel.Information,
        Message = "Assessment of alert {AlertId}: {Engine} by provider {Provider} after {Attempts} attempts, {TokensIn} tokens in, {TokensOut} out, {DurationMs} ms")]
    private partial void LogStored(Guid alertId, string engine, string? provider, int attempts, int tokensIn, int tokensOut, int durationMs);
}
