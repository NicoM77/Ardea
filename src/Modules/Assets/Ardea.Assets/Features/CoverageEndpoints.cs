using System.Text.RegularExpressions;
using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;
using Ardea.Platform.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assets.Features;

internal sealed record CoverageExample(Guid ProjectId, Guid? ComponentId, string Name, string? Detail);

/// <summary>One gap in the profile: what it is, how many, where, and a few examples.</summary>
internal sealed record CoverageHint(string Kind, string Severity, int Count, IReadOnlyList<Guid> ProjectIds, IReadOnlyList<CoverageExample> Examples);

internal sealed record CoverageView(int Components, int FullyCheckable, IReadOnlyList<CoverageHint> Hints);

/// <summary>
/// Hints on gaps in the profile (master plan 10.2, 11.6 step 6, 16.3; task 2.8): what Ardea can check
/// and what not. Hints, never locks; the wizard and the project page show them.
/// </summary>
internal static partial class CoverageEndpoints
{
    /// <summary>Package ecosystems with a source and a version comparison in the MVP (master plan 5.6, 16.3).</summary>
    public static readonly string[] CheckableEcosystems = ["npm", "nuget", "composer", "pypi", "maven", "golang"];

    private const int MaxExamples = 10;

    public static void Map(IEndpointRouteBuilder api) =>
        api.MapOrganization().WithTags("Projects")
            .MapGet("/profile/coverage", CoverageAsync).WithName("getProfileCoverage").Produces<CoverageView>().Problems(403, 404);

    private static async Task<IResult> CoverageAsync(Guid? projectId, AssetsDbContext db, IProductCatalog catalog, CancellationToken cancellationToken)
    {
        var projects = await db.Projects.AsNoTracking().Where(p => p.ArchivedAt == null && (projectId == null || p.Id == projectId))
            .Select(p => p.Id).ToListAsync(cancellationToken);
        if (projectId is not null && projects.Count == 0)
        {
            return ApiProblems.Problem("project_not_found", StatusCodes.Status404NotFound, "No such project.");
        }

        var components = await db.Components.AsNoTracking().Where(c => projects.Contains(c.ProjectId))
            .Select(c => new Item(c.Id, c.ProjectId, c.ProductKey, c.CustomName, c.Kind, c.Version, c.Packaging, c.Origin, c.Purl, c.ParentComponentId))
            .ToListAsync(cancellationToken);
        var hints = new List<CoverageHint>();
        void Add(string kind, string severity, IReadOnlyCollection<Item> items, Func<Item, string?>? detail = null)
        {
            if (items.Count > 0)
            {
                hints.Add(new CoverageHint(kind, severity, items.Count, [.. items.Select(i => i.ProjectId).Distinct()],
                    [.. items.Take(MaxExamples).Select(i => new CoverageExample(i.ProjectId, i.Id, i.Name, detail?.Invoke(i)))]));
            }
        }

        var products = new Dictionary<string, CatalogProduct?>(StringComparer.Ordinal);
        foreach (var key in components.Select(c => c.ProductKey).OfType<string>().Distinct())
        {
            products[key] = await catalog.FindByKeyAsync(key, cancellationToken);
        }

        var packages = components.Where(c => c.Purl is not null).ToList();
        var platforms = components.Where(c => c.Purl is null).ToList();

        // Without a version every finding is "unknown": the most common reason for noise or silence.
        Add("missing_version", "warning", [.. platforms.Where(c => c.Version is null && c.Kind != "service")]);

        // A version line from an image tag or a project file ("10.0"): the running version depends on the last pull.
        Add("version_line_only", "info", [.. platforms.Where(c => c.Origin == "repository" && c.Version is not null && VersionLine().IsMatch(c.Version))], c => c.Version);

        // A platform whose catalogue product has plugins, but no plugin in the profile (WordPress without plugins).
        var hosts = await PlatformsWithPluginsAsync(catalog, products.Keys, cancellationToken);
        Add("platform_without_plugins", "warning", [.. platforms.Where(c => c.ProductKey is not null && hosts.Contains(c.ProductKey) && !components.Any(o => o.ParentId == c.Id))]);

        // Distributions backport fixes without changing the upstream version: version checks are unreliable.
        Add("distribution_packages", "info", [.. components.Where(c => c.Packaging == "distribution")]);

        // A package with a range from a manifest but no lock file: "version unknown" (16.3).
        Add("packages_without_lockfile", "warning", [.. packages.Where(c => c.Version is null)], _ => "Check in the lock file to get exact versions.");

        // Packages of ecosystems Ardea does not check yet: shown, counted, no findings (16.3).
        var notCheckable = packages.Where(c => !CheckableEcosystems.Contains(Ecosystem(c.Purl!))).ToList();
        foreach (var ecosystem in notCheckable.GroupBy(c => Ecosystem(c.Purl!)).OrderByDescending(g => g.Count()))
        {
            Add("ecosystem_not_checkable", "info", [.. ecosystem], _ => ecosystem.Key);
        }

        // A product the catalogue does not know is found only by its name: weakly without a vendor.
        Add("not_in_catalog", "info", [.. platforms.Where(c => c.ProductKey is null || products.GetValueOrDefault(c.ProductKey) is null)]);

        var suggestions = await db.ComponentSuggestions.AsNoTracking().Where(s => projects.Contains(s.ProjectId) && s.State == "open")
            .Select(s => new { s.ProjectId, s.ProductKey, s.Version }).ToListAsync(cancellationToken);
        if (suggestions.Count > 0)
        {
            hints.Add(new CoverageHint("open_suggestions", "info", suggestions.Count, [.. suggestions.Select(s => s.ProjectId).Distinct()],
                [.. suggestions.Take(MaxExamples).Select(s => new CoverageExample(s.ProjectId, null, s.ProductKey, s.Version))]));
        }

        var links = await db.RepositoryLinks.AsNoTracking().Where(l => projects.Contains(l.ProjectId) && (l.State == "partial" || l.State == "failing"))
            .Select(l => new { l.ProjectId, l.Owner, l.Name, l.State, l.StateReason }).ToListAsync(cancellationToken);
        foreach (var state in links.GroupBy(l => l.State).OrderBy(g => g.Key == "failing" ? 0 : 1))
        {
            hints.Add(new CoverageHint(state.Key == "failing" ? "repositories_failing" : "repositories_partial", state.Key == "failing" ? "warning" : "info", state.Count(),
                [.. state.Select(l => l.ProjectId).Distinct()], [.. state.Take(MaxExamples).Select(l => new CoverageExample(l.ProjectId, null, $"{l.Owner}/{l.Name}", l.StateReason))]));
        }

        var gaps = new HashSet<Guid>(hints.Where(h => h.Kind is "missing_version" or "version_line_only" or "distribution_packages" or "packages_without_lockfile" or "ecosystem_not_checkable" or "not_in_catalog")
            .SelectMany(h => components.Where(c => IsIn(h.Kind, c, products, hosts, components)).Select(c => c.Id)));
        return Results.Ok(new CoverageView(components.Count, components.Count - gaps.Count, hints));
    }

    /// <summary>Whether an item counts for a gap kind, for the number of fully checkable components.</summary>
    private static bool IsIn(string kind, Item c, Dictionary<string, CatalogProduct?> products, HashSet<string> hosts, List<Item> all) => kind switch
    {
        "missing_version" => c.Purl is null && c.Version is null && c.Kind != "service",
        "version_line_only" => c.Purl is null && c.Origin == "repository" && c.Version is not null && VersionLine().IsMatch(c.Version),
        "distribution_packages" => c.Packaging == "distribution",
        "packages_without_lockfile" => c.Purl is not null && c.Version is null,
        "ecosystem_not_checkable" => c.Purl is not null && !CheckableEcosystems.Contains(Ecosystem(c.Purl)),
        "not_in_catalog" => c.Purl is null && (c.ProductKey is null || products.GetValueOrDefault(c.ProductKey) is null),
        _ => false,
    };

    /// <summary>Catalogue products that are the platform of some plugin, among those in the profile.</summary>
    private static async Task<HashSet<string>> PlatformsWithPluginsAsync(IProductCatalog catalog, IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (await catalog.HasPluginsAsync(key, cancellationToken))
            {
                result.Add(key);
            }
        }

        return result;
    }

    private static string Ecosystem(string purl) => purl.Length > 4 && purl.IndexOf('/', 4) is var slash and > 4 ? purl[4..slash].ToLowerInvariant() : "unknown";

    private sealed record Item(Guid Id, Guid ProjectId, string? ProductKey, string? CustomName, string Kind, string? Version, string Packaging, string Origin, string? Purl, Guid? ParentId)
    {
        public string Name => CustomName ?? ProductKey ?? "?";
    }

    [GeneratedRegex(@"^\d+(\.\d+)?$")]
    private static partial Regex VersionLine();
}
