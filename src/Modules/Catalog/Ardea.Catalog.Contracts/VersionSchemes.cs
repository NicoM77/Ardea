using System.Diagnostics.CodeAnalysis;

namespace Ardea.Catalog.Contracts;

/// <summary>Names of the version schemes (master plan 6.6), as stored in <c>version_scheme</c> columns.</summary>
public static class VersionSchemeNames
{
    /// <summary>Semantic Versioning 2.0; also npm.</summary>
    public const string SemVer = "semver";

    /// <summary>Go modules: SemVer with <c>v</c> prefix, pseudo-versions and <c>+incompatible</c>.</summary>
    public const string Go = "go";

    /// <summary>Composer (Packagist): up to four numbers, <c>v</c> prefix, stability suffixes.</summary>
    public const string Packagist = "packagist";

    /// <summary>NuGet: SemVer with a fourth number and NuGet's normalisation.</summary>
    public const string NuGet = "nuget";

    /// <summary>PEP 440 (PyPI).</summary>
    public const string Pep440 = "pep440";

    /// <summary>Maven <c>ComparableVersion</c>.</summary>
    public const string Maven = "maven";

    /// <summary>RPM: <c>epoch:version-release</c> compared with rpmvercmp.</summary>
    public const string Rpm = "rpm";

    /// <summary>Tolerant comparison of numeric dot sequences; the fallback for <c>custom</c> and missing types.</summary>
    public const string Numeric = "numeric";

    /// <summary>Git commits: stored, never evaluated (no scheme implements it).</summary>
    public const string Git = "git";
}

/// <summary>
/// A version read by a scheme. Versions compare only within their scheme; equal means equal
/// precedence, which can hold for different spellings (Maven <c>1.0</c> and <c>1</c>).
/// </summary>
public abstract class ParsedVersion : IComparable<ParsedVersion>, IEquatable<ParsedVersion>
{
    protected ParsedVersion(string scheme, string original, string normalized)
    {
        Scheme = scheme;
        Original = original;
        Normalized = normalized;
    }

    public string Scheme { get; }

    /// <summary>The text as given.</summary>
    public string Original { get; }

    /// <summary>The scheme's normal form, e.g. PEP 440 <c>1.0rc1</c> for <c>1.0-RC1</c>.</summary>
    public string Normalized { get; }

    public static bool operator <(ParsedVersion left, ParsedVersion right) => Compare(left, right) < 0;

    public static bool operator >(ParsedVersion left, ParsedVersion right) => Compare(left, right) > 0;

    public static bool operator <=(ParsedVersion left, ParsedVersion right) => Compare(left, right) <= 0;

    public static bool operator >=(ParsedVersion left, ParsedVersion right) => Compare(left, right) >= 0;

    public static bool operator ==(ParsedVersion? left, ParsedVersion? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ParsedVersion? left, ParsedVersion? right) => !(left == right);

    /// <exception cref="ArgumentException">The versions belong to different schemes.</exception>
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

    /// <summary>Only the scheme: different spellings of one version must share a hash code.</summary>
    public override int GetHashCode() => Scheme.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Normalized;

    /// <summary>Compares with a version of the same scheme.</summary>
    protected abstract int CompareToSameScheme(ParsedVersion other);

    private static int Compare(ParsedVersion left, ParsedVersion right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right);
    }
}

/// <summary>One way to read and order versions (master plan 6.6).</summary>
public interface IVersionScheme
{
    /// <summary>One of <see cref="VersionSchemeNames"/>.</summary>
    string Name { get; }

    /// <summary>Reads a version; <c>false</c> when the text is not a version of this scheme.</summary>
    bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out ParsedVersion? version);

    /// <summary>Whether the text is a version of this scheme.</summary>
    bool IsVersion([NotNullWhen(true)] string? text) => TryParse(text, out _);
}

/// <summary>The version schemes of the instance.</summary>
public interface IVersionSchemes
{
    IReadOnlyCollection<IVersionScheme> All { get; }

    /// <summary>
    /// The scheme for a name as stored or as a record names it (<c>semver</c>, <c>npm</c>, <c>python</c>,
    /// <c>maven</c>, <c>rpm</c>, …). <c>custom</c>, an empty or a missing name give the numeric fallback.
    /// <c>null</c> for <c>git</c> and for schemes without comparison (Debian, date-based): their
    /// versions are stored but give <see cref="VersionVerdict.Unknown"/>.
    /// </summary>
    IVersionScheme? Find(string? name);

    /// <summary>
    /// The scheme of a package ecosystem, by OSV ecosystem name (<c>npm</c>, <c>NuGet</c>, <c>PyPI</c>,
    /// <c>Maven</c>, <c>Go</c>, <c>Packagist</c>, <c>Red Hat</c>, …) or purl type (<c>golang</c>,
    /// <c>composer</c>, <c>pypi</c>, …); <c>null</c> when Ardea cannot compare its versions yet.
    /// </summary>
    IVersionScheme? ForEcosystem(string? ecosystem);
}
