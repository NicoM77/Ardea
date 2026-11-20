using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Ardea.Catalog.Contracts;

public sealed record Cpe
{
    public const string Any = "*";
    public const string NotApplicable = "-";

    private const int AttributeCount = 11;

    private readonly string[] _attributes;

    private Cpe(string[] attributes) => _attributes = attributes;

    public string Part => _attributes[0];

    public string Vendor => _attributes[1];

    public string Product => _attributes[2];

    public string Version => _attributes[3];

    public string Update => _attributes[4];

    public string Edition => _attributes[5];

    public string Language => _attributes[6];

    public string SoftwareEdition => _attributes[7];

    public string TargetSoftware => _attributes[8];

    public string TargetHardware => _attributes[9];

    public string Other => _attributes[10];

    public CpePair Pair => new(Vendor, Product);

    public static Cpe Parse(string text) =>
        TryParse(text, out var cpe) ? cpe : throw new FormatException($"'{text}' is not a CPE 2.3 formatted string or CPE 2.2 URI.");

    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out Cpe? cpe)
    {
        cpe = null;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string[]? attributes = null;
        if (text.StartsWith("cpe:2.3:", StringComparison.OrdinalIgnoreCase))
        {
            attributes = ParseFormattedString(text[8..]);
        }
        else if (text.StartsWith("cpe:/", StringComparison.OrdinalIgnoreCase))
        {
            attributes = ParseUri(text[5..]);
        }

        if (attributes is null || attributes[0] is not ("a" or "o" or "h" or Any))
        {
            return false;
        }

        cpe = new Cpe(attributes);
        return true;
    }

    public bool Matches(Cpe target)
    {
        ArgumentNullException.ThrowIfNull(target);
        for (var i = 0; i < AttributeCount; i++)
        {
            if (!AttributeMatches(_attributes[i], target._attributes[i]))
            {
                return false;
            }
        }

        return true;
    }

    public bool Equals(Cpe? other) => other is not null && _attributes.AsSpan().SequenceEqual(other._attributes);

    public override int GetHashCode() => string.Join(':', _attributes).GetHashCode(StringComparison.Ordinal);

    public override string ToString() => "cpe:2.3:" + string.Join(':', _attributes);

    internal static bool HasWildcard(string canonical) => Tokens(canonical).Exists(t => t.Wildcard);

    internal static bool AttributeMatches(string pattern, string value)
    {
        if (pattern == Any || value == Any)
        {
            return true;
        }

        if (pattern == NotApplicable || value == NotApplicable)
        {
            return pattern == value;
        }

        var p = Tokens(pattern);
        if (!p.Any(t => t.Wildcard))
        {
            return pattern == value;
        }

        var v = Tokens(value);
        return v.Any(t => t.Wildcard) ? pattern == value : Glob(p, 0, v, 0);
    }

    private static bool Glob(List<(char Char, bool Wildcard)> pattern, int pi, List<(char Char, bool Wildcard)> value, int vi)
    {
        while (pi < pattern.Count)
        {
            var (c, wildcard) = pattern[pi];
            if (wildcard && c == '*')
            {
                for (var skip = vi; skip <= value.Count; skip++)
                {
                    if (Glob(pattern, pi + 1, value, skip))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (vi >= value.Count || (!(wildcard && c == '?') && value[vi].Char != c))
            {
                return false;
            }

            pi++;
            vi++;
        }

        return vi == value.Count;
    }

    private static List<(char Char, bool Wildcard)> Tokens(string canonical)
    {
        var tokens = new List<(char, bool)>(canonical.Length);
        for (var i = 0; i < canonical.Length; i++)
        {
            if (canonical[i] == '\\' && i + 1 < canonical.Length)
            {
                tokens.Add((canonical[++i], false));
            }
            else
            {
                tokens.Add((canonical[i], canonical[i] is '*' or '?'));
            }
        }

        return tokens;
    }

    private static string[]? ParseFormattedString(string body)
    {
        var raw = SplitUnescaped(body, ':');
        if (raw.Count is 0 or > AttributeCount)
        {
            return null;
        }

        var attributes = new string[AttributeCount];
        for (var i = 0; i < AttributeCount; i++)
        {
            if (i >= raw.Count)
            {
                attributes[i] = Any;
                continue;
            }

            var value = CanonicalFromFormatted(raw[i]);
            if (value is null)
            {
                return null;
            }

            attributes[i] = value;
        }

        return attributes;
    }

    private static string[]? ParseUri(string body)
    {
        var raw = body.Split(':');
        if (raw.Length > 7)
        {
            return null;
        }

        var attributes = Enumerable.Repeat(Any, AttributeCount).ToArray();
        for (var i = 0; i < raw.Length; i++)
        {
            if (i == 5 && raw[i].StartsWith('~'))
            {
                var packed = raw[i].Split('~');
                if (packed.Length != 6)
                {
                    return null;
                }

                for (var j = 1; j < 6; j++)
                {
                    var value = CanonicalFromUri(packed[j]);
                    if (value is null)
                    {
                        return null;
                    }

                    attributes[j == 1 ? 5 : j + 5] = value;
                }

                continue;
            }

            var single = CanonicalFromUri(raw[i]);
            if (single is null)
            {
                return null;
            }

            attributes[i] = single;
        }

        return attributes;
    }

    private static string? CanonicalFromFormatted(string raw)
    {
        if (raw.Length == 0)
        {
            return null;
        }

        if (raw is Any or NotApplicable)
        {
            return raw;
        }

        var builder = new StringBuilder(raw.Length + 4);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '\\')
            {
                if (i + 1 >= raw.Length)
                {
                    return null;
                }

                AppendLiteral(builder, raw[++i]);
            }
            else if (c is '*' or '?')
            {
                builder.Append(c);
            }
            else
            {
                AppendLiteral(builder, c);
            }
        }

        return Finish(builder);
    }

    private static string? CanonicalFromUri(string raw)
    {
        if (raw.Length == 0)
        {
            return Any;
        }

        if (raw == NotApplicable)
        {
            return NotApplicable;
        }

        var builder = new StringBuilder(raw.Length + 4);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c != '%')
            {
                AppendLiteral(builder, c);
                continue;
            }

            if (i + 2 >= raw.Length || !byte.TryParse(raw.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
            {
                return null;
            }

            i += 2;
            switch (code)
            {
                case 0x01:
                    builder.Append('?');
                    break;
                case 0x02:
                    builder.Append('*');
                    break;
                default:
                    AppendLiteral(builder, (char)code);
                    break;
            }
        }

        return Finish(builder);
    }

    private static void AppendLiteral(StringBuilder builder, char c)
    {
        if (char.IsLetterOrDigit(c) || c is '_' or '.' or '-')
        {
            builder.Append(char.ToLowerInvariant(c));
        }
        else
        {
            builder.Append('\\').Append(c);
        }
    }

    private static string Finish(StringBuilder builder)
    {
        var value = builder.ToString();
        return value == NotApplicable ? "\\-" : value;
    }

    internal static List<string> SplitUnescaped(string text, char separator)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == separator)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }
}

public readonly record struct CpePair(string Vendor, string Product)
{
    public static bool TryParse([NotNullWhen(true)] string? text, out CpePair pair)
    {
        pair = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = Cpe.SplitUnescaped(text.Trim(), ':');
        if (parts.Count != 2 || !Cpe.TryParse($"cpe:2.3:a:{parts[0]}:{parts[1]}", out var cpe)
            || cpe.Vendor is Cpe.Any or Cpe.NotApplicable || cpe.Product is Cpe.Any or Cpe.NotApplicable
            || Cpe.HasWildcard(cpe.Vendor) || Cpe.HasWildcard(cpe.Product))
        {
            return false;
        }

        pair = cpe.Pair;
        return true;
    }

    public override string ToString() => Vendor + ":" + Product;
}
