using System.Diagnostics.CodeAnalysis;

namespace Ardea.Catalog.Contracts;

public static class VersionSchemeNames
{
    public const string SemVer = "semver";

    public const string Go = "go";

    public const string Packagist = "packagist";

    public const string NuGet = "nuget";

    public const string Pep440 = "pep440";

    public const string Maven = "maven";

    public const string Rpm = "rpm";

    public const string Numeric = "numeric";

    public const string Git = "git";
}

public abstract class ParsedVersion : IComparable<ParsedVersion>, IEquatable<ParsedVersion>
{
    protected ParsedVersion(string scheme, string original, string normalized)
    {
        Scheme = scheme;
        Original = original;
        Normalized = normalized;
    }

    public string Scheme { get; }

    public string Original { get; }

    public string Normalized { get; }

    public static bool operator <(ParsedVersion left, ParsedVersion right) => Compare(left, right) < 0;

    public static bool operator >(ParsedVersion left, ParsedVersion right) => Compare(left, right) > 0;

    public static bool operator <=(ParsedVersion left, ParsedVersion right) => Compare(left, right) <= 0;

    public static bool operator >=(ParsedVersion left, ParsedVersion right) => Compare(left, right) >= 0;

    public static bool operator ==(ParsedVersion? left, ParsedVersion? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ParsedVersion? left, ParsedVersion? right) => !(left == right);

    public int CompareTo(ParsedVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (!string.Equals(other.Scheme, Scheme, StringComparison.Ordinal))
        {
            throw new ArgumentException($"A {Scheme} version cannot be compared with a {other.Scheme} version.", nameof(other));
        }

        return Math.Sign(CompareToSameScheme(other));
    }

    public bool Equals(ParsedVersion? other) =>
        other is not null && string.Equals(other.Scheme, Scheme, StringComparison.Ordinal) && CompareToSameScheme(other) == 0;

    public override bool Equals(object? obj) => obj is ParsedVersion other && Equals(other);

    public override int GetHashCode() => Scheme.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Normalized;

    protected abstract int CompareToSameScheme(ParsedVersion other);

    private static int Compare(ParsedVersion left, ParsedVersion right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right);
    }
}

public interface IVersionScheme
{
    string Name { get; }

    bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out ParsedVersion? version);

    bool IsVersion([NotNullWhen(true)] string? text) => TryParse(text, out _);
}

public interface IVersionSchemes
{
    IReadOnlyCollection<IVersionScheme> All { get; }

    IVersionScheme? Find(string? name);

    IVersionScheme? ForEcosystem(string? ecosystem);
}
