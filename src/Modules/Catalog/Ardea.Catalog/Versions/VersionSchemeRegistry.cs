using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Versions;

/// <summary>The six schemes of the MVP (master plan 6.6) behind <see cref="IVersionSchemes"/>; stateless.</summary>
internal sealed class VersionSchemeRegistry : IVersionSchemes
{
    public static readonly VersionSchemeRegistry Instance = new();

    private readonly Dictionary<string, IVersionScheme> _byName;
    private readonly Dictionary<string, IVersionScheme> _byEcosystem;

    private VersionSchemeRegistry()
    {
        IVersionScheme[] all =
        [
            new SemVerScheme(VersionSchemeNames.SemVer),
            new SemVerScheme(VersionSchemeNames.Go),
            new ComposerScheme(),
            new NuGetScheme(),
            new Pep440Scheme(),
            new MavenScheme(),
            new RpmScheme(),
            new NumericScheme(),
        ];
        All = all;
        var named = all.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        // Version types as records write them (CVE JSON 5 versionType, OSV range types).
        _byName = new Dictionary<string, IVersionScheme>(named, StringComparer.OrdinalIgnoreCase)
        {
            ["npm"] = named[VersionSchemeNames.SemVer],
            ["golang"] = named[VersionSchemeNames.Go],
            ["composer"] = named[VersionSchemeNames.Packagist],
            ["python"] = named[VersionSchemeNames.Pep440],
            ["pypi"] = named[VersionSchemeNames.Pep440],
            ["pep-440"] = named[VersionSchemeNames.Pep440],
            ["custom"] = named[VersionSchemeNames.Numeric],
            ["other"] = named[VersionSchemeNames.Numeric],
            ["string"] = named[VersionSchemeNames.Numeric],
        };

        // OSV ecosystem names and purl types; Debian, Alpine and other schemes without comparison stay out.
        _byEcosystem = new Dictionary<string, IVersionScheme>(StringComparer.OrdinalIgnoreCase)
        {
            ["npm"] = named[VersionSchemeNames.SemVer],
            ["go"] = named[VersionSchemeNames.Go],
            ["golang"] = named[VersionSchemeNames.Go],
            ["packagist"] = named[VersionSchemeNames.Packagist],
            ["composer"] = named[VersionSchemeNames.Packagist],
            ["nuget"] = named[VersionSchemeNames.NuGet],
            ["pypi"] = named[VersionSchemeNames.Pep440],
            ["maven"] = named[VersionSchemeNames.Maven],
            ["rpm"] = named[VersionSchemeNames.Rpm],
            ["red hat"] = named[VersionSchemeNames.Rpm],
            ["almalinux"] = named[VersionSchemeNames.Rpm],
            ["rocky linux"] = named[VersionSchemeNames.Rpm],
            ["suse"] = named[VersionSchemeNames.Rpm],
            ["opensuse"] = named[VersionSchemeNames.Rpm],
            ["mageia"] = named[VersionSchemeNames.Rpm],
            ["openeuler"] = named[VersionSchemeNames.Rpm],
        };
    }

    public IReadOnlyCollection<IVersionScheme> All { get; }

    public IVersionScheme? Find(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return _byName[VersionSchemeNames.Numeric];
        }

        return _byName.GetValueOrDefault(trimmed);
    }

    public IVersionScheme? ForEcosystem(string? ecosystem)
    {
        var trimmed = ecosystem?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        // OSV qualifies some ecosystems with a release, e.g. «Red Hat:enterprise_linux:9» or «openSUSE:Leap 15.5».
        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        return _byEcosystem.GetValueOrDefault(colon > 0 ? trimmed[..colon] : trimmed);
    }
}
