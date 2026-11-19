using Ardea.Assets.GitHub;
using Ardea.Assets.Persistence;
using Ardea.Platform.Audit;
using Ardea.Platform.Http;
using Ardea.Platform.Persistence;
using Ardea.Platform.Secrets;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assets.Features;

internal sealed record CreateConnectionRequest(string? Name, string? Token, string? HostId);

internal sealed record UpdateConnectionRequest(string? Name, string? Token);

internal sealed record ConnectionView(
    Guid Id,
    string Name,
    string? HostId,
    string HostName,
    string TokenKind,
    string? TokenHint,
    DateTimeOffset? TokenExpiresAt,
    bool ExpiresSoon,
    IReadOnlyList<string> Warnings,
    string? Login,
    DateTimeOffset? LastOkAt,
    string? LastError,
    DateTimeOffset CreatedAt);

internal sealed record RepositoryView(string Owner, string Name, bool Private, string DefaultBranch, DateTimeOffset? PushedAt);

internal sealed record EnterpriseHostView(string Id, string Name, string ApiUrl);

internal sealed record CreateEnterpriseHostRequest(string? Name, string? ApiUrl);

/// <summary>
/// Accesses to GitHub (master plan 10.2, 16.4; task 2.10). Owners and administrators add and remove
/// them; members choose repositories from them. A token is written, never read back.
/// </summary>
internal static class ConnectionEndpoints
{
    /// <summary>From this long before its expiry, a token is flagged (master plan 16.4).</summary>
    public static readonly TimeSpan ExpiryWarning = TimeSpan.FromDays(14);

    private const string TokenPurpose = "assets.repository-connection.token";

    public static void Map(IEndpointRouteBuilder api)
    {
        var org = api.MapOrganization().WithTags("Repository connections");
        org.MapGet("/repository-connections", ListAsync).WithName("listRepositoryConnections").Produces<IReadOnlyList<ConnectionView>>().Problems(403, 404);
        org.MapPost("/repository-connections", CreateAsync).WithName("createRepositoryConnection").RequireOrganizationRole(OrganizationRole.Admin)
            .Produces<ConnectionView>(StatusCodes.Status201Created).Problems(400, 403, 404);
        org.MapPatch("/repository-connections/{id:guid}", UpdateAsync).WithName("updateRepositoryConnection").RequireOrganizationRole(OrganizationRole.Admin)
            .Produces<ConnectionView>().Problems(400, 403, 404);
        org.MapDelete("/repository-connections/{id:guid}", DeleteAsync).WithName("deleteRepositoryConnection").RequireOrganizationRole(OrganizationRole.Admin)
            .NoContent().Problems(403, 404);
        org.MapPost("/repository-connections/{id:guid}/test", TestAsync).WithName("testRepositoryConnection").RequireOrganizationRole(OrganizationRole.Member)
            .Produces<ConnectionView>().Problems(403, 404);
        org.MapGet("/repository-connections/{id:guid}/repositories", RepositoriesAsync).WithName("listConnectionRepositories")
            .RequireOrganizationRole(OrganizationRole.Member).Produces<IReadOnlyList<RepositoryView>>().Problems(403, 404, 502);

        // The organisations' choice of servers; only the instance administrator changes it (16.9).
        api.MapGet("/github-hosts", HostsAsync).WithTags("Repository connections").WithName("listGitHubHosts").RequireAuthorization();
        var admin = api.MapGroup("/instance/github-hosts").WithTags("Repository connections").RequireAuthorization(Policies.InstanceAdmin);
        admin.MapPost("/", AddHostAsync).WithName("addGitHubHost").Produces<EnterpriseHostView>(StatusCodes.Status201Created).Problems(400);
        admin.MapDelete("/{hostId}", RemoveHostAsync).WithName("removeGitHubHost").NoContent().Problems(404, 409);
    }

    private static async Task<IReadOnlyList<ConnectionView>> ListAsync(AssetsDbContext db, GitHubHosts hosts, TimeProvider time, CancellationToken cancellationToken)
    {
        var connections = await db.RepositoryConnections.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);
        var known = await hosts.ListAsync(cancellationToken);
        return [.. connections.Select(c => View(c, known, time.GetUtcNow()))];
    }

    private static async Task<IResult> CreateAsync(
        CreateConnectionRequest request, HttpContext http, AssetsDbContext db, GitHubHosts hosts, GitHubApi github, ISecretProtector secrets,
        IUnitOfWork unitOfWork, IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(name) || name.Length > 100)
        {
            errors["name"] = ["Enter a name of at most 100 characters."];
        }

        var host = await hosts.ResolveAsync(string.IsNullOrWhiteSpace(request.HostId) ? null : request.HostId, cancellationToken);
        if (host is null)
        {
            errors["hostId"] = ["No such server."];
        }

        if (errors.Count > 0)
        {
            return Validation(errors);
        }

        var now = time.GetUtcNow();
        var connection = new RepositoryConnection { Id = Guid.CreateVersion7(), Name = name!, HostId = host!.Id, CreatedBy = http.OrganizationAccess().UserId, CreatedAt = now };
        if (!string.IsNullOrWhiteSpace(request.Token))
        {
            var problem = await SetTokenAsync(connection, request.Token.Trim(), host, github, secrets, now, cancellationToken);
            if (problem is not null)
            {
                return problem;
            }
        }

        db.RepositoryConnections.Add(connection);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("repository_connection.created", "repository_connection", connection.Id.ToString(),
            new { connection.Name, connection.HostId, connection.TokenKind, connection.TokenHint }), cancellationToken);
        var view = View(connection, await hosts.ListAsync(cancellationToken), now);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Created($"repository-connections/{connection.Id}", view);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateConnectionRequest request, AssetsDbContext db, GitHubHosts hosts, GitHubApi github, ISecretProtector secrets,
        IUnitOfWork unitOfWork, IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        var connection = await db.RepositoryConnections.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null)
        {
            return NotFound();
        }

        var name = request.Name?.Trim();
        if (request.Name is not null && (string.IsNullOrEmpty(name) || name.Length > 100))
        {
            return Validation(new() { ["name"] = ["Enter a name of at most 100 characters."] });
        }

        connection.Name = name ?? connection.Name;
        var now = time.GetUtcNow();
        if (request.Token is not null)
        {
            if (request.Token.Trim().Length == 0)
            {
                // An empty token turns the connection into one for public repositories only.
                (connection.TokenEnc, connection.TokenHint, connection.TokenKind, connection.TokenScopes, connection.TokenExpiresAt, connection.Login) =
                    (null, null, "none", [], null, null);
            }
            else if (await hosts.ResolveAsync(connection.HostId, cancellationToken) is not { } host)
            {
                return Validation(new() { ["hostId"] = ["The server of this connection is no longer registered."] });
            }
            else if (await SetTokenAsync(connection, request.Token.Trim(), host, github, secrets, now, cancellationToken) is { } problem)
            {
                return problem;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("repository_connection.changed", "repository_connection", id.ToString(),
            new { connection.Name, tokenReplaced = request.Token is not null, connection.TokenHint }), cancellationToken);
        var view = View(connection, await hosts.ListAsync(cancellationToken), now);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(view);
    }

    /// <summary>Deleting the access is how a token is revoked in Ardea (master plan 16.9).</summary>
    private static async Task<IResult> DeleteAsync(Guid id, AssetsDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, CancellationToken cancellationToken)
    {
        var deleted = await db.RepositoryConnections.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            return NotFound();
        }

        await audit.RecordAsync(new AuditEntry("repository_connection.deleted", "repository_connection", id.ToString()), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>Asks GitHub again: is the token still valid, until when, for whom.</summary>
    private static async Task<IResult> TestAsync(
        Guid id, AssetsDbContext db, GitHubHosts hosts, GitHubApi github, ISecretProtector secrets, IUnitOfWork unitOfWork, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var connection = await db.RepositoryConnections.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null)
        {
            return NotFound();
        }

        var now = time.GetUtcNow();
        connection.LastCheckedAt = now;
        var host = await hosts.ResolveAsync(connection.HostId, cancellationToken);
        var token = connection.TokenEnc is null ? null : secrets.Unprotect(TokenPurpose, connection.TokenEnc);
        if (host is null)
        {
            connection.LastError = "The server of this connection is no longer registered.";
        }
        else if (connection.TokenEnc is not null && token is null)
        {
            connection.LastError = "The stored token cannot be decrypted; enter it again.";
        }
        else if (token is null)
        {
            (connection.LastOkAt, connection.LastError) = (now, null);
        }
        else
        {
            var check = await github.CheckTokenAsync(host, token, cancellationToken);
            connection.LastError = check.Valid ? null : check.Error;
            if (check.Valid)
            {
                (connection.LastOkAt, connection.TokenExpiresAt, connection.Login, connection.TokenScopes) = (now, check.ExpiresAt, check.Login, [.. check.ClassicScopes]);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var view = View(connection, await hosts.ListAsync(cancellationToken), now);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(view);
    }

    private static async Task<IResult> RepositoriesAsync(
        Guid id, string? q, AssetsDbContext db, GitHubHosts hosts, GitHubApi github, ISecretProtector secrets, CancellationToken cancellationToken)
    {
        var connection = await db.RepositoryConnections.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null)
        {
            return NotFound();
        }

        var host = await hosts.ResolveAsync(connection.HostId, cancellationToken);
        var token = connection.TokenEnc is null ? null : secrets.Unprotect(TokenPurpose, connection.TokenEnc);
        if (host is null || (connection.TokenEnc is not null && token is null))
        {
            return ApiProblems.Problem("connection_unusable", StatusCodes.Status502BadGateway, "This connection cannot be used; check it on the access page.");
        }

        try
        {
            var repositories = await github.RepositoriesAsync(host, token, q, cancellationToken);
            return Results.Ok(repositories.Select(r => new RepositoryView(r.Owner, r.Name, r.Private, r.DefaultBranch, r.PushedAt)).ToList());
        }
        catch (HttpRequestException)
        {
            return ApiProblems.Problem("github_unavailable", StatusCodes.Status502BadGateway, "GitHub did not answer as expected. Try again or check the access.");
        }
    }

    private static async Task<IReadOnlyList<EnterpriseHostView>> HostsAsync(GitHubHosts hosts, CancellationToken cancellationToken) =>
        [.. (await hosts.ListAsync(cancellationToken)).Select(h => new EnterpriseHostView(h.Id, h.Name, h.ApiUrl))];

    private static async Task<IResult> AddHostAsync(
        CreateEnterpriseHostRequest request, GitHubHosts hosts, ITenantContext tenant, IUnitOfWork unitOfWork, IAuditLog audit, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var (url, error) = await GitHubHosts.CheckAddressAsync(request.ApiUrl, cancellationToken);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(name) || name.Length > 100)
        {
            errors["name"] = ["Enter a name of at most 100 characters."];
        }

        if (error is not null)
        {
            errors["apiUrl"] = [error];
        }

        if (errors.Count > 0)
        {
            return Validation(errors);
        }

        var list = (await hosts.ListAsync(cancellationToken)).ToList();
        var host = new EnterpriseHost(Guid.CreateVersion7().ToString("N")[..12], name!, url!.AbsoluteUri);
        list.Add(host);
        await hosts.SaveAsync(list, tenant.UserId, cancellationToken);
        await audit.RecordAsync(new AuditEntry("instance.github_host_added", "github_host", host.Id, new { host.Name, host.ApiUrl }) { InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Created($"github-hosts/{host.Id}", new EnterpriseHostView(host.Id, host.Name, host.ApiUrl));
    }

    /// <summary>Connections of organisations on the server stop working; they show it on their next check.</summary>
    private static async Task<IResult> RemoveHostAsync(
        string hostId, GitHubHosts hosts, ITenantContext tenant, IUnitOfWork unitOfWork, IAuditLog audit, CancellationToken cancellationToken)
    {
        var list = (await hosts.ListAsync(cancellationToken)).ToList();
        if (list.RemoveAll(h => h.Id == hostId) == 0)
        {
            return ApiProblems.Problem("github_host_not_found", StatusCodes.Status404NotFound, "No such server.");
        }

        await hosts.SaveAsync(list, tenant.UserId, cancellationToken);
        await audit.RecordAsync(new AuditEntry("instance.github_host_removed", "github_host", hostId) { InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>Checks the token with GitHub and stores it encrypted; a token GitHub refuses is not stored.</summary>
    private static async Task<IResult?> SetTokenAsync(
        RepositoryConnection connection, string token, GitHubHost host, GitHubApi github, ISecretProtector secrets, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var check = await github.CheckTokenAsync(host, token, cancellationToken);
        if (!check.Valid)
        {
            return ApiProblems.Validation("invalid_token", "GitHub does not accept this token.", new Dictionary<string, string[]> { ["token"] = [check.Error ?? "GitHub does not accept this token."] });
        }

        connection.TokenEnc = secrets.Protect(TokenPurpose, token);
        connection.TokenHint = token[^Math.Min(4, token.Length)..];
        connection.TokenKind = check.Kind;
        connection.TokenScopes = [.. check.ClassicScopes];
        connection.TokenExpiresAt = check.ExpiresAt;
        connection.Login = check.Login;
        connection.LastCheckedAt = connection.LastOkAt = now;
        connection.LastError = null;
        return null;
    }

    private static ConnectionView View(RepositoryConnection c, IReadOnlyList<EnterpriseHost> hosts, DateTimeOffset now)
    {
        var warnings = new List<string>();
        if (c.TokenKind == "classic")
        {
            warnings.Add(c.TokenScopes.Length == 0
                ? "A classic token; prefer a fine-grained, read-only token for selected repositories."
                : $"A classic token with the scopes {string.Join(", ", c.TokenScopes)}; prefer a fine-grained, read-only token for selected repositories.");
        }

        if (c.TokenKind != "none" && c.TokenExpiresAt is null)
        {
            warnings.Add("The token does not expire; prefer one with an expiry date.");
        }

        var hostName = c.HostId is null ? "github.com" : hosts.FirstOrDefault(h => h.Id == c.HostId)?.Name ?? "(removed server)";
        var expiresSoon = c.TokenExpiresAt is { } expires && expires - now <= ExpiryWarning;
        return new ConnectionView(c.Id, c.Name, c.HostId, hostName, c.TokenKind, c.TokenHint, c.TokenExpiresAt, expiresSoon, warnings, c.Login, c.LastOkAt, c.LastError, c.CreatedAt);
    }

    private static IResult Validation(Dictionary<string, string[]> errors) =>
        ApiProblems.Validation("validation_failed", "Some values are missing or not valid.", errors);

    private static IResult NotFound() => ApiProblems.Problem("repository_connection_not_found", StatusCodes.Status404NotFound, "No such repository connection.");
}
