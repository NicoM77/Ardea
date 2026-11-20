using Ardea.Assessment.Persistence;
using Ardea.Platform.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Features;

internal sealed record ModelQualityView(
    string? Provider, string? Model, string? PromptVersion, long Assessments, long Verdicts, long Confirmed, long Corrected, double? CorrectionRate);

internal static class ModelQualityEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/instance/ai").WithTags("Administration").RequireAuthorization(Policies.InstanceAdmin);
        admin.MapGet("/quality", ListAsync).WithName("listModelQuality").Produces<IReadOnlyList<ModelQualityView>>();
    }

    private static async Task<IResult> ListAsync(AssessDbContext db, CancellationToken cancellationToken) => Results.Ok(await ReadAsync(db, cancellationToken));

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
