using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;
using Ardea.Platform.Audit;
using Ardea.Platform.Http;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assets.Features;

internal sealed record CreateProjectRequest(string? Name, string? Description, string? Criticality, string? Exposure);

internal sealed record UpdateProjectRequest(string? Name, string? Description, string? Criticality, string? Exposure, bool? Archived);

internal sealed record ProjectView(
    Guid Id, string Name, string? Description, string Criticality, string Exposure, bool Archived, int ComponentCount, DateTimeOffset CreatedAt, DateTimeOffset ChangedAt);

internal sealed record ComponentView(
    Guid Id,
    string? ProductKey,
    string Name,
    string? Vendor,
    bool InCatalog,
    string Kind,
    string? Version,
    string? VersionScheme,
    string Packaging,
    Guid? ParentId,
    string? Exposure,
    string? Criticality,
    string Protection,
    string? ProtectionNote,
    string Origin,
    string? Purl,
    string? DependencyScope,
    string? DependencyRelation,
    CatalogMatchView? CatalogMatch = null);

/// <summary>
/// A free entry the catalogue now knows (master plan 6.9 «Vorschläge»): its name matches this catalogue entry, often a
/// learned one. Switching the component to the entry is a proposal for a member, never done on its own.
/// </summary>
/// <param name="Key">The catalogue key to switch to.</param>
/// <param name="Name">The entry's name.</param>
/// <param name="Learned">The match comes from an entry the instance learned.</param>
internal sealed record CatalogMatchView(string Key, string Name, bool Learned);

internal sealed record ProjectDetail(ProjectView Project, IReadOnlyList<ComponentView> Components);

internal sealed record ReplaceComponentsRequest(IReadOnlyList<ComponentInput>? Components);

internal sealed record ReplaceComponentsResponse(int Created, int Updated, int Removed, int Unchanged, IReadOnlyList<ComponentView> Components);

/// <summary>
/// Projects and their components (master plan 4.5, 4.9, 10.2; task 2.3). Every change to a profile
/// is audited and queues the reverse search for the project.
/// </summary>
internal static class ProjectEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var org = api.MapOrganization().WithTags("Projects");
        org.MapGet("/projects", ListAsync).WithName("listProjects").Produces<IReadOnlyList<ProjectView>>().Problems(403, 404);
        org.MapPost("/projects", CreateAsync).WithName("createProject").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<ProjectView>(StatusCodes.Status201Created).Problems(400, 403, 404);
        org.MapGet("/projects/{projectId:guid}", GetAsync).WithName("getProject").Produces<ProjectDetail>().Problems(403, 404);
        org.MapPatch("/projects/{projectId:guid}", UpdateAsync).WithName("updateProject").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<ProjectView>().Problems(400, 403, 404);
        org.MapDelete("/projects/{projectId:guid}", ArchiveAsync).WithName("archiveProject").RequireOrganizationRole(OrganizationRole.Member)
            .NoContent().Problems(403, 404);

        org.MapGet("/projects/{projectId:guid}/components", ComponentsAsync).WithName("listComponents").Produces<IReadOnlyList<ComponentView>>().Problems(403, 404);
        org.MapPost("/projects/{projectId:guid}/components", AddComponentAsync).WithName("addComponent").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<ComponentView>(StatusCodes.Status201Created).Problems(400, 403, 404);
        org.MapPut("/projects/{projectId:guid}/components", ReplaceComponentsAsync).WithName("replaceComponents").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<ReplaceComponentsResponse>().Problems(400, 403, 404);
        org.MapPatch("/projects/{projectId:guid}/components/{id:guid}", UpdateComponentAsync).WithName("updateComponent").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<ComponentView>().Problems(400, 403, 404, 409);
        org.MapDelete("/projects/{projectId:guid}/components/{id:guid}", RemoveComponentAsync).WithName("removeComponent").RequireOrganizationRole(OrganizationRole.Member)
            .NoContent().Problems(403, 404, 409);
        org.MapPost("/projects/{projectId:guid}/components/{id:guid}/detach", DetachAsync).WithName("detachComponent").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<ComponentView>().Problems(403, 404, 409);
    }

    private static async Task<IReadOnlyList<ProjectView>> ListAsync(bool? archived, AssetsDbContext db, CancellationToken cancellationToken)
    {
        var projects = await db.Projects.AsNoTracking().Where(p => archived == true || p.ArchivedAt == null)
            .Select(p => new { Project = p, Count = db.Components.Count(c => c.ProjectId == p.Id) })
            .ToListAsync(cancellationToken);
        return [.. projects.OrderBy(p => p.Project.Name, StringComparer.CurrentCultureIgnoreCase).Select(p => View(p.Project, p.Count))];
    }

    private static async Task<IResult> CreateAsync(
        CreateProjectRequest request, AssetsDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        var errors = CheckProject(request.Name, request.Description, request.Criticality, request.Exposure, nameRequired: true);
        if (errors.Count > 0)
        {
            return Validation(errors);
        }

        var now = time.GetUtcNow();
        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name!.Trim(),
            Description = Trimmed(request.Description),
            Criticality = request.Criticality?.Trim().ToLowerInvariant() ?? "normal",
            Exposure = request.Exposure?.Trim().ToLowerInvariant() ?? "internal",
            CreatedAt = now,
            ChangedAt = now,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("project.created", "project", project.Id.ToString(), new { project.Name, project.Criticality, project.Exposure }), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Created($"projects/{project.Id}", View(project, 0));
    }

    private static async Task<IResult> GetAsync(Guid projectId, AssetsDbContext db, IProductCatalog catalog, CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return ProjectNotFound();
        }

        var components = await ViewsAsync(db, catalog, projectId, cancellationToken);
        return Results.Ok(new ProjectDetail(View(project, components.Count), components));
    }

    private static async Task<IResult> UpdateAsync(
        Guid projectId, UpdateProjectRequest request, AssetsDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, IJobQueue jobs, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var errors = CheckProject(request.Name, request.Description, request.Criticality, request.Exposure, nameRequired: false);
        if (errors.Count > 0)
        {
            return Validation(errors);
        }

        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return ProjectNotFound();
        }

        var (criticality, exposure, archived) = (project.Criticality, project.Exposure, project.ArchivedAt is not null);
        project.Name = Trimmed(request.Name) ?? project.Name;
        project.Description = request.Description is null ? project.Description : Trimmed(request.Description);
        project.Criticality = request.Criticality?.Trim().ToLowerInvariant() ?? project.Criticality;
        project.Exposure = request.Exposure?.Trim().ToLowerInvariant() ?? project.Exposure;
        if (request.Archived is { } archive && archive != archived)
        {
            project.ArchivedAt = archive ? time.GetUtcNow() : null;
        }

        project.ChangedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("project.changed", "project", projectId.ToString(), request), cancellationToken);

        // Exposure, criticality and archiving change what a finding means; the name does not.
        if (criticality != project.Criticality || exposure != project.Exposure || archived != (project.ArchivedAt is not null))
        {
            await jobs.EnqueueAsync(new ProjectProfileChanged(projectId), ProjectProfileChanged.Options(projectId), cancellationToken);
        }

        var count = await db.Components.CountAsync(c => c.ProjectId == projectId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(View(project, count));
    }

    /// <summary>
    /// Archives the project: it leaves lists and the matching, but its history stays (master plan 4.5,
    /// <c>archived_at</c>). <c>PATCH {"archived": false}</c> brings it back.
    /// </summary>
    private static async Task<IResult> ArchiveAsync(
        Guid projectId, AssetsDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, IJobQueue jobs, TimeProvider time, CancellationToken cancellationToken)
    {
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return ProjectNotFound();
        }

        if (project.ArchivedAt is null)
        {
            project.ArchivedAt = project.ChangedAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(new AuditEntry("project.archived", "project", projectId.ToString()), cancellationToken);
            await jobs.EnqueueAsync(new ProjectProfileChanged(projectId), ProjectProfileChanged.Options(projectId), cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ComponentsAsync(Guid projectId, AssetsDbContext db, IProductCatalog catalog, CancellationToken cancellationToken) =>
        await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken)
            ? Results.Ok(await ViewsAsync(db, catalog, projectId, cancellationToken))
            : ProjectNotFound();

    private static async Task<IResult> AddComponentAsync(
        Guid projectId, ComponentInput request, AssetsDbContext db, IProductCatalog catalog, IUnitOfWork unitOfWork, IAuditLog audit, IJobQueue jobs,
        TimeProvider time, CancellationToken cancellationToken)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return ProjectNotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var values = await ComponentRules.CheckAsync(request, catalog, errors, string.Empty, cancellationToken);
        if (request.ParentId is { } parentId && !await db.Components.AnyAsync(c => c.Id == parentId && c.ProjectId == projectId, cancellationToken))
        {
            errors.TryAdd("parentId", ["No such component in this project."]);
        }

        if (values is null || errors.Count > 0)
        {
            return Validation(errors);
        }

        var now = time.GetUtcNow();
        var component = new Component { Id = Guid.CreateVersion7(), ProjectId = projectId, Kind = values.Kind, ParentComponentId = request.ParentId, CreatedAt = now, ChangedAt = now };
        values.ApplyTo(component);
        db.Components.Add(component);
        await db.SaveChangesAsync(cancellationToken);
        await ComponentKeys.RefreshAsync(db, catalog, [component], cancellationToken);
        await ProfileChangedAsync(audit, jobs, projectId, "component.added", component.Id, values, cancellationToken);

        // The view reads the catalogue through the unit of work: before the commit, never after (ADR 023).
        var view = await ViewAsync(component, catalog, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Created($"projects/{projectId}/components/{component.Id}", view);
    }

    /// <summary>
    /// Changes the given fields; <c>null</c> keeps a field, an empty string clears it. A component from
    /// a repository changes only in its context (exposure, criticality, protection); its product and
    /// version belong to the repository until it is detached (master plan 16.5).
    /// </summary>
    private static async Task<IResult> UpdateComponentAsync(
        Guid projectId, Guid id, ComponentInput request, AssetsDbContext db, IProductCatalog catalog, IUnitOfWork unitOfWork, IAuditLog audit,
        IJobQueue jobs, TimeProvider time, CancellationToken cancellationToken)
    {
        var component = await db.Components.SingleOrDefaultAsync(c => c.Id == id && c.ProjectId == projectId, cancellationToken);
        if (component is null)
        {
            return ComponentNotFound();
        }

        var changesIdentity = request.ProductKey is not null || request.CustomName is not null || request.CustomVendor is not null
            || request.Kind is not null || request.Version is not null || request.Packaging is not null;
        if (component.Origin == "repository" && changesIdentity)
        {
            return RepositoryComponent();
        }

        var custom = request.ProductKey is null && (request.CustomName is not null || component.ProductKey is null);
        var merged = new ComponentInput(
            null,
            null,
            custom ? null : request.ProductKey ?? component.ProductKey,
            custom ? request.CustomVendor ?? component.CustomVendor : null,
            custom ? request.CustomName ?? component.CustomName : null,
            request.Kind ?? component.Kind,
            request.Version ?? component.Version,
            request.Packaging ?? component.Packaging,
            null,
            null,
            request.Exposure ?? component.Exposure,
            request.Criticality ?? component.Criticality,
            request.Protection ?? component.Protection,
            request.ProtectionNote ?? component.ProtectionNote);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var values = await ComponentRules.CheckAsync(merged, catalog, errors, string.Empty, cancellationToken);
        if (request.ParentId is { } parentId && (parentId == id || !await db.Components.AnyAsync(c => c.Id == parentId && c.ProjectId == projectId, cancellationToken)))
        {
            errors.TryAdd("parentId", ["No such component in this project."]);
        }

        if (values is null || errors.Count > 0)
        {
            return Validation(errors);
        }

        var parentChanged = request.ParentId is not null && request.ParentId != component.ParentComponentId;
        if (!values.SameAs(component) || parentChanged)
        {
            values.ApplyTo(component);
            component.ParentComponentId = request.ParentId ?? component.ParentComponentId;
            component.ChangedAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            await ComponentKeys.RefreshAsync(db, catalog, [component], cancellationToken);
            await ProfileChangedAsync(audit, jobs, projectId, "component.changed", id, values, cancellationToken);
            var changed = await ViewAsync(component, catalog, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Results.Ok(changed);
        }

        return Results.Ok(await ViewAsync(component, catalog, cancellationToken));
    }

    private static async Task<IResult> RemoveComponentAsync(
        Guid projectId, Guid id, AssetsDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, IJobQueue jobs, CancellationToken cancellationToken)
    {
        var component = await db.Components.SingleOrDefaultAsync(c => c.Id == id && c.ProjectId == projectId, cancellationToken);
        if (component is null)
        {
            return ComponentNotFound();
        }

        if (component.Origin == "repository")
        {
            return RepositoryComponent();
        }

        db.Components.Remove(component);
        await db.SaveChangesAsync(cancellationToken);
        await ProfileChangedAsync(audit, jobs, projectId, "component.removed", id, new { component.ProductKey, component.CustomName, component.Version }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>Takes a component away from its repository link: it becomes manual and no reading changes it again (master plan 16.5).</summary>
    private static async Task<IResult> DetachAsync(
        Guid projectId, Guid id, AssetsDbContext db, IProductCatalog catalog, IUnitOfWork unitOfWork, IAuditLog audit, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var component = await db.Components.SingleOrDefaultAsync(c => c.Id == id && c.ProjectId == projectId, cancellationToken);
        if (component is null)
        {
            return ComponentNotFound();
        }

        if (component.Origin != "repository")
        {
            return ApiProblems.Problem("already_manual", StatusCodes.Status409Conflict, "This component is already maintained by hand.");
        }

        component.Origin = "manual";
        component.RepositoryLinkId = null;
        component.ChangedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("component.detached", "component", id.ToString(), new { projectId }), cancellationToken);
        var view = await ViewAsync(component, catalog, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(view);
    }

    /// <summary>
    /// Replaces the manual components as a whole, as the wizard sends them (master plan 4.9, 10.2): an
    /// item keeps the component with its id or, without one, the component with the same product and
    /// parent; only what the new list lacks is removed. Components from a repository stay untouched.
    /// </summary>
    private static async Task<IResult> ReplaceComponentsAsync(
        Guid projectId, ReplaceComponentsRequest request, AssetsDbContext db, IProductCatalog catalog, IUnitOfWork unitOfWork, IAuditLog audit,
        IJobQueue jobs, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return ProjectNotFound();
        }

        var items = request.Components ?? [];
        var existing = await db.Components.Where(c => c.ProjectId == projectId).ToListAsync(cancellationToken);
        var result = await ComponentReconciler.ReconcileAsync(projectId, items, existing, catalog, time.GetUtcNow(), cancellationToken);
        if (result.Errors.Count > 0)
        {
            return Validation(result.Errors);
        }

        db.Components.AddRange(result.Created);
        db.Components.RemoveRange(result.Removed);
        await db.SaveChangesAsync(cancellationToken);
        await ComponentKeys.RefreshAsync(db, catalog, [.. result.Created, .. result.Updated], cancellationToken);
        if (result.Created.Count + result.Updated.Count + result.Removed.Count > 0)
        {
            await ProfileChangedAsync(audit, jobs, projectId, "profile.replaced", projectId, new
            {
                created = result.Created.Select(c => c.Id),
                updated = result.Updated.Select(c => c.Id),
                removed = result.Removed.Select(c => c.Id),
            }, cancellationToken, subjectType: "project");
        }

        var views = await ViewsAsync(db, catalog, projectId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(new ReplaceComponentsResponse(result.Created.Count, result.Updated.Count, result.Removed.Count, result.Unchanged, views));
    }

    private static async Task ProfileChangedAsync(
        IAuditLog audit, IJobQueue jobs, Guid projectId, string action, Guid subject, object data, CancellationToken cancellationToken, string subjectType = "component")
    {
        await audit.RecordAsync(new AuditEntry(action, subjectType, subject.ToString(), data), cancellationToken);
        await jobs.EnqueueAsync(new ProjectProfileChanged(projectId), ProjectProfileChanged.Options(projectId), cancellationToken);
    }

    private static async Task<IReadOnlyList<ComponentView>> ViewsAsync(AssetsDbContext db, IProductCatalog catalog, Guid projectId, CancellationToken cancellationToken)
    {
        var components = await db.Components.AsNoTracking().Where(c => c.ProjectId == projectId).ToListAsync(cancellationToken);
        var views = new List<ComponentView>(components.Count);
        foreach (var component in components)
        {
            views.Add(await ViewAsync(component, catalog, cancellationToken));
        }

        return [.. views.OrderBy(v => v.Kind, StringComparer.Ordinal).ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static async Task<ComponentView> ViewAsync(Component c, IProductCatalog catalog, CancellationToken cancellationToken)
    {
        var product = c.ProductKey is null ? null : await catalog.FindByKeyAsync(c.ProductKey, cancellationToken);
        var match = c.ProductKey is null && c.CustomName is not null ? await catalog.FindByNameAsync(c.CustomVendor, c.CustomName, cancellationToken) : null;
        return new ComponentView(
            c.Id, c.ProductKey, product?.Name ?? c.CustomName ?? c.ProductKey ?? "?", product?.Vendor ?? c.CustomVendor, product is not null, c.Kind,
            c.Version, c.VersionScheme, c.Packaging, c.ParentComponentId, c.Exposure, c.Criticality, c.Protection, c.ProtectionNote, c.Origin,
            c.Purl, c.DependencyScope, c.DependencyRelation,
            match is null ? null : new CatalogMatchView(match.Product.Key, match.Product.Name, match.Learned || match.Product.Origin == ProductOrigin.Learned));
    }

    private static ProjectView View(Project p, int components) =>
        new(p.Id, p.Name, p.Description, p.Criticality, p.Exposure, p.ArchivedAt is not null, components, p.CreatedAt, p.ChangedAt);

    private static Dictionary<string, string[]> CheckProject(string? name, string? description, string? criticality, string? exposure, bool nameRequired)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if ((nameRequired || name is not null) && string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Enter a name."];
        }
        else if (name?.Trim().Length > 200)
        {
            errors["name"] = ["Use at most 200 characters."];
        }

        if (description?.Trim().Length > 2000)
        {
            errors["description"] = ["Use at most 2000 characters."];
        }

        if (criticality is not null && !ComponentRules.Criticalities.Contains(criticality.Trim().ToLowerInvariant()))
        {
            errors["criticality"] = ["Choose low, normal or high."];
        }

        if (exposure is not null && !ComponentRules.Exposures.Contains(exposure.Trim().ToLowerInvariant()))
        {
            errors["exposure"] = ["Choose internet, internal or isolated."];
        }

        return errors;
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Validation(Dictionary<string, string[]> errors) =>
        ApiProblems.Validation("validation_failed", "Some values are missing or not valid.", errors);

    private static IResult ProjectNotFound() => ApiProblems.Problem("project_not_found", StatusCodes.Status404NotFound, "No such project.");

    private static IResult ComponentNotFound() => ApiProblems.Problem("component_not_found", StatusCodes.Status404NotFound, "No such component.");

    private static IResult RepositoryComponent() => ApiProblems.Problem("repository_component", StatusCodes.Status409Conflict,
        "This component comes from a repository; detach it first to change its product or version, or to remove it.");
}
