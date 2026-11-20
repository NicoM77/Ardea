using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;
using Ardea.Platform.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assets.Features;

internal sealed record StackSuggestionView(string ProductKey, string Name, string Kind, string Reason, string? Basis, IReadOnlyList<string> Projects);

internal static class StackSuggestions
{
    public const string Companion = "companion";
    public const string FromProjects = "project";
    public const string Common = "common";

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
