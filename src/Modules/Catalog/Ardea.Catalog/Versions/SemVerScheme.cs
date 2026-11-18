using System.Diagnostics.CodeAnalysis;
using Ardea.Catalog.Contracts;
using Semver;

namespace Ardea.Catalog.Versions;

/// <summary>
/// Semantic Versioning 2.0 precedence, through the Semver library. Used for <c>semver</c> (npm) and
/// <c>go</c>: a leading <c>v</c>, leading zeros and missing minor or patch numbers are accepted; build
/// metadata such as Go's <c>+incompatible</c> does not count; Go pseudo-versions
/// (<c>v0.0.0-20220127200216-cd36cc0744dd</c>) are pre-releases and order correctly as such.
/// </summary>
internal sealed class SemVerScheme(string name) : IVersionScheme
{
    public string Name => name;

    public bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out ParsedVersion? version)
    {
        version = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !char.IsAsciiDigit(trimmed.TrimStart('v', 'V').FirstOrDefault())
            || !SemVersion.TryParse(trimmed, SemVersionStyles.Any, out var parsed))
        {
            return false;
        }

        version = new SemVerVersion(name, trimmed, parsed);
        return true;
    }

    private sealed class SemVerVersion(string scheme, string original, SemVersion value)
        : ParsedVersion(scheme, original, value.WithoutMetadata().ToString())
    {
        protected override int CompareToSameScheme(ParsedVersion other) =>
            value.ComparePrecedenceTo(((SemVerVersion)other).Value);

        private SemVersion Value => value;
    }
}
