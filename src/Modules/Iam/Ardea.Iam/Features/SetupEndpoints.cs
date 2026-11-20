using Ardea.Iam.Domain;
using Ardea.Iam.Persistence;
using Ardea.Platform.Audit;
using Ardea.Platform.Http;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Iam.Features;

internal sealed record UpdateSetupRequest(string? StepDone, Guid? FirstProjectId);

internal sealed record SetupView(IReadOnlyList<string> StepsDone, Guid? FirstProjectId, DateTimeOffset? CompletedAt);

internal static class SetupEndpoints
{
    public static readonly string[] Steps = ["organization", "stack", "project", "repository", "operations", "technologies", "coverage", "notifications", "overview"];

    public static void Map(IEndpointRouteBuilder api)
    {
        var org = api.MapOrganization().WithTags("Organizations");
        org.MapGet("/setup", GetAsync).WithName("getWizardState").Produces<SetupView>().Problems(403, 404);
        org.MapPatch("/setup", UpdateAsync).WithName("updateWizardState").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<SetupView>().Problems(400, 403, 404);
        org.MapPost("/setup/complete", CompleteAsync).WithName("completeWizard").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<SetupView>().Problems(403, 404);
    }

    private static async Task<IResult> GetAsync(IamDbContext db, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().SingleAsync(cancellationToken);
        return Results.Ok(View(organization.Settings.Setup));
    }

    private static async Task<IResult> UpdateAsync(
        UpdateSetupRequest request, IamDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, CancellationToken cancellationToken)
    {
        if (request.StepDone is not null && !Steps.Contains(request.StepDone))
        {
            return IamErrors.Validation(new() { ["stepDone"] = [$"Choose one of {string.Join(", ", Steps)}."] });
        }

        var organization = await db.Organizations.SingleAsync(cancellationToken);
        var before = organization.Settings.Setup ?? new SetupState();
        var steps = request.StepDone is null || before.StepsDone.Contains(request.StepDone)
            ? before.StepsDone
            : [.. Steps.Where(s => s == request.StepDone || before.StepsDone.Contains(s)), .. before.StepsDone.Where(s => !Steps.Contains(s))];
        var after = before with { StepsDone = steps, FirstProjectId = request.FirstProjectId ?? before.FirstProjectId };
        if (!after.StepsDone.SequenceEqual(before.StepsDone) || after.FirstProjectId != before.FirstProjectId)
        {
            organization.Settings = organization.Settings with { Setup = after };
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(new AuditEntry("org.setup_saved", "organization", organization.OrganizationId.ToString(), request), cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return Results.Ok(View(after));
    }

    private static async Task<IResult> CompleteAsync(
        IamDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.SingleAsync(cancellationToken);
        var setup = organization.Settings.Setup ?? new SetupState();
        if (setup.CompletedAt is null)
        {
            setup = setup with { CompletedAt = time.GetUtcNow() };
            organization.Settings = organization.Settings with { Setup = setup };
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(new AuditEntry("org.setup_completed", "organization", organization.OrganizationId.ToString()), cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return Results.Ok(View(setup));
    }

    private static SetupView View(SetupState? setup) =>
        new(setup?.StepsDone ?? [], setup?.FirstProjectId, setup?.CompletedAt);
}
