using System.Diagnostics.CodeAnalysis;
using Ardea.Catalog.Contracts;
using NuGet.Versioning;

namespace Ardea.Catalog.Versions;

internal sealed class NuGetScheme : IVersionScheme
{
    public string Name => VersionSchemeNames.NuGet;

    public bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out ParsedVersion? version)
    {
        version = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !NuGetVersion.TryParse(trimmed, out var parsed))
        {
            return false;
        }

        version = new NuGetParsedVersion(trimmed, parsed);
        return true;
    }

    private sealed class NuGetParsedVersion(string original, NuGetVersion value)
        : ParsedVersion(VersionSchemeNames.NuGet, original, value.ToNormalizedString())
    {
        protected override int CompareToSameScheme(ParsedVersion other) =>
            VersionComparer.Default.Compare(value, ((NuGetParsedVersion)other).Value);

        private NuGetVersion Value => value;
    }
}
