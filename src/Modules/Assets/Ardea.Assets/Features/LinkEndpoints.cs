using System.Text.Json;
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
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assets.Features;

internal sealed record CreateLinkRequest(Guid? ConnectionId, string? Owner, string? Name, string? Ref, string? Path);

internal sealed record UpdateLinkRequest(string? Ref, string? Path);

internal sealed record LinkView(
    Guid Id,
    Guid ConnectionId,
    string ConnectionName,
    string Owner,
    string Name,
    string? Ref,
    string? Path,
    string State,
    string? StateReason,
    DateTimeOffset? FailingSince,
    string? LastCommitSha,
    DateTimeOffset? LastIndexedAt,
    DateTimeOffset NextCheckAt,
    bool ShrinkPending,
    int ComponentCount);

internal sealed record SuggestionEvidenceView(string File, int Line, string Value);

internal sealed record SuggestionView(Guid Id, Guid LinkId, string ProductKey, string ProductName, string? Version, IReadOnlyList<SuggestionEvidenceView> Evidence, string State, Guid? ComponentId);

internal sealed record DecideSuggestionRequest(string? Decision, string? Version);

/// <summary>
/// Repositories that feed a project, and the suggestions their platform files give (master plan
/// 10.2, 16.2, 16.5, 16.6; task 2.13). Members connect repositories and decide suggestions.
/// </summary>
internal static class LinkEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var org = api.MapOrganization().WithTags("Repositories");
        org.MapGet("/projects/{projectId:guid}/repositories", ListAsync).WithName("listRepositoryLinks").Produces<IReadOnlyList<LinkView>>().Problems(403, 404);
        org.MapPost("/projects/{projectId:guid}/repositories", CreateAsync).WithName("createRepositoryLink").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<LinkView>(StatusCodes.Status201Created).Problems(400, 403, 404, 409);
        org.MapPatch("/projects/{projectId:guid}/repositories/{linkId:guid}", UpdateAsync).WithName("updateRepositoryLink").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<LinkView>().Problems(400, 403, 404);
        org.MapDelete("/projects/{projectId:guid}/repositories/{linkId:guid}", DeleteAsync).WithName("deleteRepositoryLink").RequireOrganizationRole(OrganizationRole.Member)
            .NoContent().Problems(403, 404);
        org.MapPost("/projects/{projectId:guid}/repositories/{linkId:guid}/sync", SyncAsync).WithName("syncRepositoryLink").RequireOrganizationRole(OrganizationRole.Member)
            .NoContent().Problems(403, 404);
        org.MapPost("/projects/{projectId:guid}/repositories/{linkId:guid}/confirm-shrink", ConfirmShrinkAsync).WithName("confirmRepositoryShrink")
            .RequireOrganizationRole(OrganizationRole.Member).NoContent().Problems(403, 404, 409);
        org.MapGet("/projects/{projectId:guid}/suggestions", SuggestionsAsync).WithName("listSuggestions").Produces<IReadOnlyList<SuggestionView>>().Problems(403, 404);
        org.MapPatch("/projects/{projectId:guid}/suggestions/{id:guid}", DecideAsync).WithName("decideSuggestion").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<SuggestionView>().Problems(400, 403, 404, 409);
    }

    private static async Task<IResult> ListAsync(Guid projectId, AssetsDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return ProjectNotFound();
        }

        var links = await db.RepositoryLinks.AsNoTracking().Where(l => l.ProjectId == projectId)
            .Select(l => new
            {
                Link = l,
                Connection = db.RepositoryConnections.Where(c => c.Id == l.ConnectionId).Select(c => c.Name).FirstOrDefault(),
                Components = db.Components.Count(c => c.RepositoryLinkId == l.Id),
            })
            .ToListAsync(cancellationToken);
        return Results.Ok(links.OrderBy(l => l.Link.Owner).ThenBy(l => l.Link.Name).Select(l => View(l.Link, l.Connection ?? "?", l.Components)).ToList());
    }

    private static async Task<IResult> CreateAsync(
        Guid projectId, CreateLinkRequest request, HttpContext http, AssetsDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return ProjectNotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var connection = request.ConnectionId is { } connectionId
            ? await db.RepositoryConnections.AsNoTracking().SingleOrDefaultAsync(c => c.Id == connectionId, cancellationToken)
            : null;
        if (connection is null)
        {
            errors["connectionId"] = ["Choose an access to GitHub."];
        }

        var owner = request.Owner?.Trim();
        var name = request.Name?.Trim();
        if (!IsGitHubName(owner))
        {
            errors["owner"] = ["Enter the owner as on GitHub."];
        }

        if (!IsGitHubName(name))
        {
            errors["name"] = ["Enter the repository name as on GitHub."];
        }

        var (reference, path) = (Optional(request.Ref), NormalizePath(request.Path, errors));
        if (reference?.Length > 250)
        {
            errors["ref"] = ["Use at most 250 characters."];
        }

        if (errors.Count > 0)
        {
            return Validation(errors);
        }

        if (await db.RepositoryLinks.AnyAsync(l => l.ProjectId == projectId && l.ConnectionId == connection!.Id && l.Owner == owner && l.Name == name && l.Path == path, cancellationToken))
        {
            return ApiProblems.Problem("link_exists", StatusCodes.Status409Conflict, "This repository already feeds the project.");
        }

        var now = time.GetUtcNow();
        var link = new RepositoryLink
        {
            Id = Guid.CreateVersion7(),
            ProjectId = projectId,
            ConnectionId = connection!.Id,
            Owner = owner!,
            Name = name!,
            Ref = reference,
            Path = path,
            NextCheckAt = now,
            CreatedBy = http.OrganizationAccess().UserId,
            CreatedAt = now,
        };
        db.RepositoryLinks.Add(link);
        await db.SaveChangesAsync(cancellationToken);
        // Newly linked: read at once (master plan 16.2).
        await http.RequestServices.GetRequiredService<IJobQueue>().EnqueueAsync(
            new Repositories.Indexing.IndexRepository(link.Id), Repositories.Indexing.IndexRepository.Options(link.Id, http.OrganizationAccess().OrganizationId), cancellationToken);
        await audit.RecordAsync(new AuditEntry("repository_link.created", "repository_link", link.Id.ToString(), new { projectId, owner, name, reference, path }), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Created($"projects/{projectId}/repositories/{link.Id}", View(link, connection.Name, 0));
    }

    /// <summary>Another branch or subfolder is a new state to read; the link is due at once.</summary>
    private static async Task<IResult> UpdateAsync(
        Guid projectId, Guid linkId, UpdateLinkRequest request, AssetsDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var link = await db.RepositoryLinks.SingleOrDefaultAsync(l => l.Id == linkId && l.ProjectId == projectId, cancellationToken);
        if (link is null)
        {
            return LinkNotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var path = request.Path is null ? link.Path : NormalizePath(request.Path, errors);
        if (errors.Count > 0)
        {
            return Validation(errors);
        }

        var reference = request.Ref is null ? link.Ref : Optional(request.Ref);
        if (reference != link.Ref || path != link.Path)
        {
            (link.Ref, link.Path, link.HeadEtag, link.NextCheckAt) = (reference, path, null, time.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(new AuditEntry("repository_link.changed", "repository_link", linkId.ToString(), new { reference, path }), cancellationToken);
        }

        var connection = await db.RepositoryConnections.Where(c => c.Id == link.ConnectionId).Select(c => c.Name).SingleAsync(cancellationToken);
        var components = await db.Components.CountAsync(c => c.RepositoryLinkId == linkId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(View(link, connection, components));
    }

    /// <summary>Removing a link removes its components (master plan 10.2): the repository no longer vouches for them.</summary>
    private static async Task<IResult> DeleteAsync(
        Guid projectId, Guid linkId, AssetsDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, IJobQueue jobs, CancellationToken cancellationToken)
    {
        var link = await db.RepositoryLinks.SingleOrDefaultAsync(l => l.Id == linkId && l.ProjectId == projectId, cancellationToken);
        if (link is null)
        {
            return LinkNotFound();
        }

        var removed = await db.Components.CountAsync(c => c.RepositoryLinkId == linkId, cancellationToken);
        db.RepositoryLinks.Remove(link);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("repository_link.deleted", "repository_link", linkId.ToString(), new { projectId, link.Owner, link.Name, removedComponents = removed }), cancellationToken);
        if (removed > 0)
        {
            await jobs.EnqueueAsync(new ProjectProfileChanged(projectId), ProjectProfileChanged.Options(projectId), cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>"Read now" (master plan 16.6): the link is due at once and its branch head is asked again.</summary>
    private static async Task<IResult> SyncAsync(
        Guid projectId, Guid linkId, AssetsDbContext db, IJobQueue jobs, IUnitOfWork unitOfWork, TimeProvider time, CancellationToken cancellationToken)
    {
        var link = await db.RepositoryLinks.SingleOrDefaultAsync(l => l.Id == linkId && l.ProjectId == projectId, cancellationToken);
        if (link is null)
        {
            return LinkNotFound();
        }

        (link.HeadEtag, link.NextCheckAt) = (null, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await jobs.EnqueueAsync(new Repositories.Indexing.IndexRepository(linkId), Repositories.Indexing.IndexRepository.Options(linkId, link.OrganizationId), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>Takes over a much smaller state that a reading held back (master plan 16.5).</summary>
    private static async Task<IResult> ConfirmShrinkAsync(
        Guid projectId, Guid linkId, AssetsDbContext db, IJobQueue jobs, IUnitOfWork unitOfWork, IAuditLog audit, CancellationToken cancellationToken)
    {
        var link = await db.RepositoryLinks.AsNoTracking().SingleOrDefaultAsync(l => l.Id == linkId && l.ProjectId == projectId, cancellationToken);
        if (link is null)
        {
            return LinkNotFound();
        }

        if (link.PendingShrink is null)
        {
            return ApiProblems.Problem("no_shrink_pending", StatusCodes.Status409Conflict, "No smaller state is waiting for confirmation.");
        }

        await jobs.EnqueueAsync(new Repositories.Indexing.IndexRepository(linkId, ApplyShrink: true), Repositories.Indexing.IndexRepository.Options(linkId, link.OrganizationId), cancellationToken);
        await audit.RecordAsync(new AuditEntry("repository.shrink_confirmed", "repository_link", linkId.ToString(), new { commit = link.PendingShrinkCommit }), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SuggestionsAsync(Guid projectId, string? state, AssetsDbContext db, IProductCatalog catalog, CancellationToken cancellationToken)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return ProjectNotFound();
        }

        state ??= "open";
        var suggestions = await db.ComponentSuggestions.AsNoTracking().Where(s => s.ProjectId == projectId && s.State == state)
            .OrderBy(s => s.ProductKey).ToListAsync(cancellationToken);
        var views = new List<SuggestionView>(suggestions.Count);
        foreach (var suggestion in suggestions)
        {
            views.Add(await ViewAsync(suggestion, catalog, cancellationToken));
        }

        return Results.Ok(views);
    }

    /// <summary>
    /// Confirms a suggestion, with a corrected version where needed: it becomes a component from the
    /// repository with its evidence (16.5). A dismissed one is not suggested again while the file says the same.
    /// </summary>
    private static async Task<IResult> DecideAsync(
        Guid projectId, Guid id, DecideSuggestionRequest request, HttpContext http, AssetsDbContext db, IProductCatalog catalog, IUnitOfWork unitOfWork,
        IAuditLog audit, IJobQueue jobs, TimeProvider time, CancellationToken cancellationToken)
    {
        var suggestion = await db.ComponentSuggestions.SingleOrDefaultAsync(s => s.Id == id && s.ProjectId == projectId, cancellationToken);
        if (suggestion is null)
        {
            return ApiProblems.Problem("suggestion_not_found", StatusCodes.Status404NotFound, "No such suggestion.");
        }

        if (request.Decision is not ("accept" or "dismiss"))
        {
            return Validation(new() { ["decision"] = ["Choose accept or dismiss."] });
        }

        if (suggestion.State != "open")
        {
            return ApiProblems.Problem("suggestion_decided", StatusCodes.Status409Conflict, "This suggestion is already decided.");
        }

        var now = time.GetUtcNow();
        (suggestion.DecidedBy, suggestion.DecidedAt) = (http.OrganizationAccess().UserId, now);
        if (request.Decision == "dismiss")
        {
            suggestion.State = "dismissed";
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(new AuditEntry("suggestion.dismissed", "suggestion", id.ToString(), new { suggestion.ProductKey, suggestion.Version }), cancellationToken);
            var dismissed = await ViewAsync(suggestion, catalog, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Results.Ok(dismissed);
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var version = request.Version is null ? suggestion.Version : Optional(request.Version);
        var values = await ComponentRules.CheckAsync(
            new ComponentInput(null, null, suggestion.ProductKey, null, null, null, version, null, null, null, null, null, null, null), catalog, errors, string.Empty, cancellationToken);
        if (values is null)
        {
            return Validation(errors);
        }

        var component = new Component { Id = Guid.CreateVersion7(), ProjectId = projectId, Kind = values.Kind, CreatedAt = now, ChangedAt = now };
        values.ApplyTo(component);
        (component.Origin, component.RepositoryLinkId, component.Evidence) = ("repository", suggestion.RepositoryLinkId, JsonDocument.Parse(suggestion.Evidence.RootElement.GetRawText()));
        db.Components.Add(component);
        (suggestion.State, suggestion.ComponentId) = ("accepted", component.Id);
        await db.SaveChangesAsync(cancellationToken);
        await ComponentKeys.RefreshAsync(db, catalog, [component], cancellationToken);
        await audit.RecordAsync(new AuditEntry("suggestion.accepted", "suggestion", id.ToString(), new { suggestion.ProductKey, version, componentId = component.Id }), cancellationToken);
        await jobs.EnqueueAsync(new ProjectProfileChanged(projectId), ProjectProfileChanged.Options(projectId), cancellationToken);
        var accepted = await ViewAsync(suggestion, catalog, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(accepted);
    }

    private static async Task<SuggestionView> ViewAsync(ComponentSuggestion s, IProductCatalog catalog, CancellationToken cancellationToken)
    {
        var product = await catalog.FindByKeyAsync(s.ProductKey, cancellationToken);
        var evidence = s.Evidence.RootElement.ValueKind == JsonValueKind.Array
            ? s.Evidence.RootElement.EnumerateArray()
                .Select(e => new SuggestionEvidenceView(e.GetProperty("file").GetString() ?? "", e.GetProperty("line").GetInt32(), e.GetProperty("value").GetString() ?? ""))
                .ToList()
            : [];
        return new SuggestionView(s.Id, s.RepositoryLinkId, s.ProductKey, product?.Name ?? s.ProductKey, s.Version, evidence, s.State, s.ComponentId);
    }

    private static LinkView View(RepositoryLink l, string connection, int components) =>
        new(l.Id, l.ConnectionId, connection, l.Owner, l.Name, l.Ref, l.Path, l.State, l.StateReason, l.FailingSince, l.LastCommitSha, l.LastIndexedAt,
            l.NextCheckAt, l.PendingShrink is not null, components);

    /// <summary>Owners and repositories: letters, digits, <c>-</c>, <c>_</c> and <c>.</c>, as GitHub allows.</summary>
    private static bool IsGitHubName(string? value) =>
        value is { Length: > 0 and <= 100 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') && value is not ("." or "..");

    /// <summary>A subfolder as a relative path with forward slashes; <c>..</c> and absolute paths are refused (16.9).</summary>
    private static string? NormalizePath(string? value, Dictionary<string, string[]> errors)
    {
        var path = Optional(value)?.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (path.Length > 500 || path.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            errors["path"] = ["Enter a subfolder of the repository, e.g. services/api."];
            return null;
        }

        return path;
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Validation(Dictionary<string, string[]> errors) =>
        ApiProblems.Validation("validation_failed", "Some values are missing or not valid.", errors);

    private static IResult ProjectNotFound() => ApiProblems.Problem("project_not_found", StatusCodes.Status404NotFound, "No such project.");

    private static IResult LinkNotFound() => ApiProblems.Problem("repository_link_not_found", StatusCodes.Status404NotFound, "No such repository link.");
}
