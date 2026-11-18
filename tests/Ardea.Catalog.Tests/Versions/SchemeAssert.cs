using Ardea.Catalog.Contracts;
using Ardea.Catalog.Versions;

namespace Ardea.Catalog.Tests.Versions;

internal static class SchemeAssert
{
    public static IVersionScheme Scheme(string name) => VersionSchemeRegistry.Instance.Find(name)!;

    public static ParsedVersion Parse(string scheme, string text)
    {
        Assert.True(Scheme(scheme).TryParse(text, out var version), $"'{text}' should be a {scheme} version");
        return version;
    }

    /// <summary>Every version is lower than the next one, in both directions of the comparison.</summary>
    public static void Ascending(string scheme, string ascending)
    {
        var versions = ascending.Split(" < ").Select(v => Parse(scheme, v)).ToList();
        for (var i = 1; i < versions.Count; i++)
        {
            Assert.True(versions[i - 1] < versions[i], $"{scheme}: expected {versions[i - 1].Original} < {versions[i].Original}");
            Assert.True(versions[i] > versions[i - 1], $"{scheme}: expected {versions[i].Original} > {versions[i - 1].Original}");
        }
    }

    public static void Same(string scheme, string left, string right)
    {
        var a = Parse(scheme, left);
        var b = Parse(scheme, right);
        Assert.True(a.CompareTo(b) == 0 && a.Equals(b), $"{scheme}: expected {left} == {right}");
    }
}
