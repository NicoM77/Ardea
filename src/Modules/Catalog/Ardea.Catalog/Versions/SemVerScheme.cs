using System.Diagnostics.CodeAnalysis;
using Ardea.Catalog.Contracts;
using Semver;

namespace Ardea.Catalog.Versions;

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
