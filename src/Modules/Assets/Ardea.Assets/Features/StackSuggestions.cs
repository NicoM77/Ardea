using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;
using Ardea.Platform.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assets.Features;

/// <summary>
/// A catalogue product the stack does not name yet, and why it is suggested (master plan 17.2, 17.8; ADR 044).
/// </summary>
/// <param name="ProductKey">The catalogue key to add.</param>
/// <param name="Name">The product's name in the catalogue.</param>
/// <param name="Kind">The product's kind, which places it in a group of the catalogue search.</param>
/// <param name="Reason">
/// <c>companion</c>: an entry of the stack does not cover it although its vulnerabilities are published under it
/// (<see cref="Basis"/> names that entry); <c>project</c>: the organisation's projects name it (<see cref="Projects"/>);
/// <c>common</c>: often in stacks (master plan 17.8).
/// </param>
/// <param name="Basis">For <c>companion</c>, the name of the stack entry it goes with.</param>
/// <param name="Projects">For <c>project</c>, the names of the projects that name it.</param>
internal sealed record StackSuggestionView(string ProductKey, string Name, string Kind, string Reason, string? Basis, IReadOnlyList<string> Projects);

/// <summary>
/// What the wizard and the stack page offer to add (task 5a.4): companions first, then the products the projects name,
/// then the usual entries of master plan 17.8. A product appears once, under its first reason, and never when the stack
/// names it already.
/// </summary>
internal static class StackSuggestions
{
    public const string Companion = "companion";
    public const string FromProjects = "project";
    public const string Common = "common";

    /// <summary>
    /// The usual entries of master plan 17.8 as catalogue products: «.NET / ASP.NET Core», «Java / Spring», «Python /
    /// Django» and «MySQL / MariaDB» are two products each; FortiOS stands for the FortiGate. Microsoft Azure is not one
    /// product in the catalogue (ADR 044) and is left out; the wizard explains that when someone looks for it.
    /// </summary>
    public static readonly string[] CommonKeys =
    [
        "angular", "react", "vue", "nodejs", "dotnet", "aspnetcore", "openjdk", "spring-framework", "php", "python", "django", "go",
        "wordpress", "umbraco", "typo3", "shopware",
        "postgresql", "sql-server", "mysql", "mariadb", "redis", "mongodb", "elasticsearch", "rabbitmq",
        "windows-server", "ubuntu", "debian", "linux-kernel",
        "nginx", "apache-httpd", "iis", "fortios",
        "docker-engine", "kubernetes",
        "exchange-server",
    ];

    /// <summary>
    /// ADR 044, consequences: IIS and Exchange are named in Windows Server records, so they go with Windows Server; a
    /// distribution covers itself, not the Linux kernel it ships.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Companions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["iis"] = "windows-server",
        ["exchange-server"] = "windows-server",
        ["ubuntu"] = "linux-kernel",
        ["debian"] = "linux-kernel",
        ["rhel"] = "linux-kernel",
        ["sles"] = "linux-kernel",
        ["rocky-linux"] = "linux-kernel",
        ["almalinux"] = "linux-kernel",
        ["oracle-linux"] = "linux-kernel",
        ["alpine-linux"] = "linux-kernel",
    };

    /// <summary>Kinds a stack entry stands for; libraries, plugins and services are what a broad entry does not cover (17.2).</summary>
    private static readonly string[] StackKinds = ["os", "runtime", "framework", "cms", "database", "messaging", "container", "network", "virtualization", "other"];

    public static void Map(IEndpointRouteBuilder api) =>
        api.MapOrganization().WithTags("Projects")
            .MapGet("/stack/suggestions", ListAsync).WithName("listStackSuggestions").Produces<IReadOnlyList<StackSuggestionView>>().Problems(403, 404);

    private static async Task<IReadOnlyList<StackSuggestionView>> ListAsync(AssetsDbContext db, IProductCatalog catalog, CancellationToken cancellationToken)
    {
        var stackKeys = await db.Components.AsNoTracking()
            .Where(c => c.ProductKey != null && db.Projects.Any(p => p.Id == c.ProjectId && p.Kind == Project.StackKind))
            .Select(c => c.ProductKey!)
            .Distinct()
            .ToListAsync(cancellationToken);
        var named = await (
                from c in db.Components.AsNoTracking()
                join p in db.Projects.AsNoTracking() on c.ProjectId equals p.Id
                where p.Kind == Project.ProjectKind && p.ArchivedAt == null && c.ProductKey != null && c.ParentComponentId == null
                    && StackKinds.Contains(c.Kind)
                select new { Key = c.ProductKey!, Project = p.Name })
            .Distinct()
            .ToListAsync(cancellationToken);

        var taken = new HashSet<string>(stackKeys, StringComparer.Ordinal);
        var suggestions = new List<StackSuggestionView>();
        async Task AddAsync(string key, string reason, string? basis, IReadOnlyList<string> projects)
        {
            if (!taken.Add(key) || await catalog.FindByKeyAsync(key, cancellationToken) is not { } product)
            {
                return;
            }

            suggestions.Add(new StackSuggestionView(product.Key, product.Name, product.Kind.ToString().ToLowerInvariant(), reason, basis, projects));
        }

        foreach (var key in stackKeys.Order(StringComparer.Ordinal))
        {
            if (Companions.TryGetValue(key, out var companion))
            {
                var entry = await catalog.FindByKeyAsync(key, cancellationToken);
                await AddAsync(companion, Companion, entry?.Name ?? key, []);
            }
        }

        foreach (var group in named.GroupBy(n => n.Key, StringComparer.Ordinal)
                     .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            await AddAsync(group.Key, FromProjects, null, [.. group.Select(g => g.Project).Order(StringComparer.CurrentCultureIgnoreCase)]);
        }

        foreach (var key in CommonKeys)
        {
            await AddAsync(key, Common, null, []);
        }

        return suggestions;
    }
}
