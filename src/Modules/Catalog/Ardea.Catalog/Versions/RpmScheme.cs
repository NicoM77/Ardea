using System.Diagnostics.CodeAnalysis;
using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Versions;

internal sealed class RpmScheme : IVersionScheme
{
    public string Name => VersionSchemeNames.Rpm;

    public bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out ParsedVersion? version)
    {
        version = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+' or '~' or '^' or ':'))
        {
            return false;
        }

        var epoch = "0";
        var rest = trimmed;
        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            epoch = trimmed[..colon];
            rest = trimmed[(colon + 1)..];
            if (epoch.Length == 0 || !epoch.All(char.IsAsciiDigit) || rest.Contains(':', StringComparison.Ordinal))
            {
                return false;
            }

            epoch = NumericText.Trim(epoch);
        }

        string? release = null;
        var dash = rest.LastIndexOf('-');
        if (dash >= 0)
        {
            release = rest[(dash + 1)..];
            rest = rest[..dash];
        }

        if (rest.Length == 0 || !char.IsAsciiDigit(rest[0]) || release is { Length: 0 })
        {
            return false;
        }

        var normalized = (epoch == "0" ? string.Empty : epoch + ":") + rest + (release is null ? string.Empty : "-" + release);
        version = new RpmVersion(trimmed, normalized, epoch, rest, release);
        return true;
    }

    internal static int Compare(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 0;
        }

        int i = 0, j = 0;
        while (i < a.Length || j < b.Length)
        {
            while (i < a.Length && !char.IsAsciiLetterOrDigit(a[i]) && a[i] is not ('~' or '^'))
            {
                i++;
            }

            while (j < b.Length && !char.IsAsciiLetterOrDigit(b[j]) && b[j] is not ('~' or '^'))
            {
                j++;
            }

            var ca = i < a.Length ? a[i] : '\0';
            var cb = j < b.Length ? b[j] : '\0';
            if (ca == '~' || cb == '~')
            {
                if (ca != '~')
                {
                    return 1;
                }

                if (cb != '~')
                {
                    return -1;
                }

                i++;
                j++;
                continue;
            }

            if (ca == '^' || cb == '^')
            {
                if (ca == '\0')
                {
                    return -1;
                }

                if (cb == '\0')
                {
                    return 1;
                }

                if (ca != '^')
                {
                    return 1;
                }

                if (cb != '^')
                {
                    return -1;
                }

                i++;
                j++;
                continue;
            }

            if (ca == '\0' || cb == '\0')
            {
                break;
            }

            var numeric = char.IsAsciiDigit(ca);
            var endA = i;
            var endB = j;
            if (numeric)
            {
                while (endA < a.Length && char.IsAsciiDigit(a[endA]))
                {
                    endA++;
                }

                while (endB < b.Length && char.IsAsciiDigit(b[endB]))
                {
                    endB++;
                }
            }
            else
            {
                while (endA < a.Length && char.IsAsciiLetter(a[endA]))
                {
                    endA++;
                }

                while (endB < b.Length && char.IsAsciiLetter(b[endB]))
                {
                    endB++;
                }
            }

            if (endB == j)
            {
                return numeric ? 1 : -1;
            }

            var c = numeric
                ? NumericText.Compare(a.AsSpan(i, endA - i), b.AsSpan(j, endB - j))
                : Math.Sign(string.CompareOrdinal(a[i..endA], b[j..endB]));
            if (c != 0)
            {
                return c;
            }

            i = endA;
            j = endB;
        }

        var restA = i < a.Length;
        var restB = j < b.Length;
        return restA == restB ? 0 : restA ? 1 : -1;
    }

    private sealed class RpmVersion(string original, string normalized, string epoch, string version, string? release)
        : ParsedVersion(VersionSchemeNames.Rpm, original, normalized)
    {
        private string Epoch => epoch;

        private string Version => version;

        private string? Release => release;

        protected override int CompareToSameScheme(ParsedVersion other)
        {
            var o = (RpmVersion)other;
            var c = NumericText.Compare(epoch, o.Epoch);
            if (c == 0)
            {
                c = Compare(version, o.Version);
            }

            if (c == 0 && release is not null && o.Release is not null)
            {
                c = Compare(release, o.Release);
            }

            return c;
        }
    }
}
