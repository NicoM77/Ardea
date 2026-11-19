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

/// <summary>A hit through a learned catalogue entry: the key it came under and the raw keys of the sources the entry maps.</summary>
internal sealed record LearnedHit(string ProductKey, IReadOnlyList<string> RawKeys);

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
/// <param name="Direct">The strong keys of the affected products, with the statements behind each.</param>
/// <param name="Platforms">Platforms of affected plugins, with the plugins.</param>
/// <param name="LearnedOnly">Product keys that only a learned catalogue entry gave (master plan 6.9): every statement
/// behind them resolved through one. A key a statement also gives without learning is not among them.</param>
internal sealed record EntryKeys(
    IReadOnlyDictionary<ProfileKey, IReadOnlyList<AffectedFacts>> Direct,
    IReadOnlyDictionary<ProfileKey, IReadOnlyList<string>> Platforms,
    IReadOnlySet<ProfileKey> LearnedOnly)
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

        var learned = new HashSet<ProfileKey>();
        var plain = new HashSet<ProfileKey>();
        foreach (var row in entry.Affected)
        {
            if (row.ProductKey is { Length: > 0 } product)
            {
                var key = new ProfileKey("product", product);
                Add(key, row);
                (row.LearnedFrom is { Length: > 0 } ? learned : plain).Add(key);
            }

            // A key from a learned entry keeps its raw key: a free entry that matched the raw key still does (master plan 6.9).
            if (row.LearnedFrom is { Length: > 0 } raw)
            {
                var key = new ProfileKey("product", raw);
                Add(key, row);
                plain.Add(key);
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

        learned.ExceptWith(plain);
        return new EntryKeys(
            direct.ToDictionary(d => d.Key, d => (IReadOnlyList<AffectedFacts>)d.Value),
            platforms.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value),
            learned);
    }

    /// <summary>
    /// How a strong hit came about through a learned catalogue entry, or <c>null</c> when the curated catalogue alone
    /// gives it (master plan 6.9: «über gelernten Katalogeintrag»). It does when every key it matched under is a product
    /// key only a learned entry gave, or belongs to a learned product the component is recorded as.
    /// </summary>
    /// <param name="candidate">The hit.</param>
    /// <param name="learnedProducts">Keys of learned products in effect among the hit's keys and the component's product.</param>
    public LearnedHit? Learned(Candidate candidate, ISet<string> learnedProducts)
    {
        if (!candidate.IsStrong)
        {
            return null;
        }

        var componentLearned = candidate.Component.ProductKey is { } own && learnedProducts.Contains(own);
        var viaLearned = candidate.Keys.All(k =>
            (k.Type == "product" && (LearnedOnly.Contains(k) || learnedProducts.Contains(k.Value))) || (k.Type != "product" && componentLearned));
        if (!viaLearned)
        {
            return null;
        }

        var product = candidate.Keys.Where(k => k.Type == "product").Select(k => k.Value).FirstOrDefault() ?? candidate.Component.ProductKey!;
        var raw = candidate.Keys.Where(Direct.ContainsKey).SelectMany(k => Direct[k]).Select(a => a.LearnedFrom).OfType<string>()
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return new LearnedHit(product, raw);
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
