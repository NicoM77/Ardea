using Ardea.Assessment.Persistence;
using Ardea.Platform.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Features;

/// <summary>How people judged one model's verdicts (master plan 7.9 «Rückmeldung im Betrieb»; task 4.7).</summary>
/// <param name="Provider">The configured provider, e.g. <c>local</c>.</param>
/// <param name="Model">The model it was asked for.</param>
/// <param name="PromptVersion">The prompt's version.</param>
/// <param name="Assessments">Checked assessments the model made with this prompt.</param>
/// <param name="Verdicts">Verdicts on components in them.</param>
/// <param name="Confirmed">Reviews that confirmed a verdict that counted.</param>
/// <param name="Corrected">Reviews that corrected its verdict or the level it led to.</param>
/// <param name="CorrectionRate">Corrected among the reviewed, from 0 to 1; <c>null</c> before the first review.</param>
internal sealed record ModelQualityView(
    string? Provider, string? Model, string? PromptVersion, long Assessments, long Verdicts, long Confirmed, long Corrected, double? CorrectionRate);

/// <summary>
/// The correction rate per model and prompt version for the instance administration (master plan 7.9, 11.4). Counts
/// over all organisations, through the system function <c>assess.model_feedback_counts()</c> (ADR 029): no texts, no ids.
/// </summary>
internal static class ModelQualityEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/instance/ai").WithTags("Administration").RequireAuthorization(Policies.InstanceAdmin);
        admin.MapGet("/quality", ListAsync).WithName("listModelQuality").Produces<IReadOnlyList<ModelQualityView>>();
    }

    private static async Task<IResult> ListAsync(AssessDbContext db, CancellationToken cancellationToken) => Results.Ok(await ReadAsync(db, cancellationToken));

    /// <summary>The rows, ordered by provider, model and prompt version.</summary>
    internal static async Task<IReadOnlyList<ModelQualityView>> ReadAsync(AssessDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<Counts>($"""
            SELECT provider, model, prompt_version, assessments, verdicts, confirmed, corrected FROM assess.model_feedback_counts()
            """).ToListAsync(cancellationToken);
        return rows
            .OrderBy(r => r.Provider, StringComparer.Ordinal).ThenBy(r => r.Model, StringComparer.Ordinal).ThenBy(r => r.PromptVersion, StringComparer.Ordinal)
            .Select(r => new ModelQualityView(
                r.Provider, r.Model, r.PromptVersion, r.Assessments, r.Verdicts, r.Confirmed, r.Corrected,
                r.Confirmed + r.Corrected == 0 ? null : Math.Round((double)r.Corrected / (r.Confirmed + r.Corrected), 4)))
            .ToList();
    }

    private sealed record Counts(string? Provider, string? Model, string? PromptVersion, long Assessments, long Verdicts, long Confirmed, long Corrected);
}
