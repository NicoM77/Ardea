using Ardea.Assets.Contracts;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Contracts;

namespace Ardea.Assessment.Matching;

/// <summary>How an entry hit a component (master plan 7.2).</summary>
internal static class MatchTypes
{
    public const string Package = "package";
    public const string Cpe = "cpe";
    public const string Product = "product";
    public const string Variant = "variant";
    public const string Ecosystem = "ecosystem";
    public const string Name = "name";

    public static bool IsStrong(string matchType) => matchType is Package or Cpe or Product;
}

/// <summary>An entry's hit on one component, with the affected products that caused it.</summary>
/// <param name="Component">The component hit.</param>
/// <param name="MatchType">One of <see cref="MatchTypes"/>.</param>
/// <param name="Keys">The component's keys that matched, e.g. <c>purl pkg:npm/lodash</c>.</param>
/// <param name="Affected">For a strong hit, the entry's version statements about the matched product.</param>
internal sealed record Candidate(ProfileComponent Component, string MatchType, IReadOnlyList<ProfileKey> Keys, IReadOnlyList<AffectedFacts> Affected)
{
    public bool IsStrong => MatchTypes.IsStrong(MatchType);
}

/// <summary>
/// The keys of an entry in the forms of <c>assets.component_key</c> (master plan 7.2): product keys, CPE
/// pairs and packages of its affected products, and the platforms of affected plugins for the weak
/// «ecosystem» hit.
/// </summary>
internal sealed record EntryKeys(
    IReadOnlyDictionary<ProfileKey, IReadOnlyList<AffectedFacts>> Direct,
    IReadOnlyDictionary<ProfileKey, IReadOnlyList<string>> Platforms)
{
    public IReadOnlyList<ProfileKey> All => [.. Direct.Keys.Concat(Platforms.Keys).Distinct()];

    public static async Task<EntryKeys> ForAsync(EntryFacts entry, IProductCatalog catalog, CancellationToken cancellationToken)
    {
        var direct = new Dictionary<ProfileKey, List<AffectedFacts>>();
        void Add(ProfileKey key, AffectedFacts row)
        {
            if (!direct.TryGetValue(key, out var rows))
            {
                direct[key] = rows = [];
            }

            rows.Add(row);
        }

        foreach (var row in entry.Affected)
        {
            if (row.ProductKey is { Length: > 0 } product)
            {
                Add(new ProfileKey("product", product), row);
            }

            if (row.CpeVendor is { Length: > 0 } vendor && row.CpeProduct is { Length: > 0 } cpeProduct)
            {
                Add(new ProfileKey("cpe", new CpePair(vendor, cpeProduct).ToString()), row);
            }

            if (row.PurlType is { Length: > 0 } type && row.PurlName is { Length: > 0 } name)
            {
                Add(new ProfileKey("purl", new PackageCoordinate(type, name).ToString()), row);
            }
        }

        // A plugin's platform: a hit there is weak, «platform in use, component not in the profile».
        var platforms = new Dictionary<ProfileKey, List<string>>();
        foreach (var productKey in entry.Affected.Select(a => a.ProductKey).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            if (await catalog.FindByKeyAsync(productKey, cancellationToken) is { PlatformKey: { Length: > 0 } platform })
            {
                var key = new ProfileKey("product", platform);
                if (!platforms.TryGetValue(key, out var plugins))
                {
                    platforms[key] = plugins = [];
                }

                plugins.Add(productKey);
            }
        }

        return new EntryKeys(
            direct.ToDictionary(d => d.Key, d => (IReadOnlyList<AffectedFacts>)d.Value),
            platforms.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value));
    }

    /// <summary>
    /// The candidates among the given components (master plan 7.2). A component hit under a strong key
    /// is a strong candidate with the affected products of every strong key that matched; otherwise
    /// a weak key gives a «variant» (a related catalogue product) or «name» (a free entry without
    /// vendor) candidate. A platform whose plugin the entry names is a weak «ecosystem» candidate,
    /// unless the project already has a strong hit for the entry.
    /// </summary>
    public IReadOnlyList<Candidate> Match(IEnumerable<ProfileComponent> components, ISet<string> catalogKeys)
    {
        var result = new List<Candidate>();
        var list = components.ToList();
        foreach (var component in list)
        {
            var strong = component.StrongKeys.Where(Direct.ContainsKey).ToList();
            if (strong.Count > 0)
            {
                var type = strong.Any(k => k.Type == "purl") ? MatchTypes.Package : strong.Any(k => k.Type == "cpe") ? MatchTypes.Cpe : MatchTypes.Product;
                var affected = strong.SelectMany(k => Direct[k]).Distinct().ToList();
                result.Add(new Candidate(component, type, strong, affected));
                continue;
            }

            var weak = component.WeakKeys.Where(Direct.ContainsKey).ToList();
            if (weak.Count > 0)
            {
                var type = weak.Any(k => k.Type == "product" && catalogKeys.Contains(k.Value)) ? MatchTypes.Variant : MatchTypes.Name;
                result.Add(new Candidate(component, type, weak, []));
            }
        }

        var strongProjects = result.Where(c => c.IsStrong).Select(c => c.Component.ProjectId).ToHashSet();
        var matched = result.Select(c => c.Component.Id).ToHashSet();
        foreach (var component in list.Where(c => !matched.Contains(c.Id) && !strongProjects.Contains(c.ProjectId)))
        {
            var platform = component.StrongKeys.Where(Platforms.ContainsKey).ToList();
            if (platform.Count > 0)
            {
                result.Add(new Candidate(component, MatchTypes.Ecosystem, platform, []));
            }
        }

        return result;
    }
}
