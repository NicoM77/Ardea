using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Ardea.Catalog.Contracts;

/// <summary>
/// The key of a product (master plan 6.5): vendor and product in lower case, without legal forms,
/// special characters and known suffixes, written <c>vendor/product</c>, or only <c>product</c> when
/// the vendor is missing or <c>n/a</c>. The catalogue maps spellings to its own keys; a product the
/// catalogue does not know keeps the raw key built here.
/// </summary>
public readonly record struct ProductKey
{
    private static readonly HashSet<string> IgnoredVendors = new(StringComparer.Ordinal)
    {
        "", "na", "n/a", "-", "*", "none", "unknown", "unspecified", "notapplicable", "not applicable",
    };

    /// <summary>Legal forms, removed from the end of a vendor name (and a leading «the»).</summary>
    private static readonly HashSet<string> LegalForms = new(StringComparer.Ordinal)
    {
        "inc", "incorporated", "corp", "corporation", "co", "company", "ltd", "limited", "llc", "llp", "lp", "plc",
        "gmbh", "mbh", "ag", "kg", "se", "sa", "sas", "sarl", "srl", "spa", "bv", "nv", "oy", "oyj", "ab", "as", "asa",
        "aps", "kk", "pty", "pvt", "pte", "sl", "sro", "zrt", "kft",
    };

    /// <summary>Known suffixes that do not distinguish vendors, removed from the end of a vendor name.</summary>
    private static readonly string[][] VendorSuffixes =
    [
        ["software", "foundation"], ["global", "development", "group"], ["development", "group"], ["development", "team"],
        ["foundation"], ["software"], ["project"], ["team"], ["community"], ["group"], ["technologies"], ["labs"],
        ["organization"], ["org"], ["systems"], ["networks"],
    ];

    /// <summary>Known suffixes of product names that do not distinguish products.</summary>
    private static readonly string[][] ProductSuffixes =
    [
        ["wordpress", "plugin"], ["plugin", "for", "wordpress"], ["for", "wordpress"], ["plugin"],
    ];

    private ProductKey(string value) => Value = value;

    /// <summary>The key, e.g. <c>microsoft/aspnetcore</c> or <c>postgresql</c>.</summary>
    public string Value { get; }

    /// <summary>The vendor part, or <c>null</c> for a key without vendor.</summary>
    public string? Vendor => Value.IndexOf('/', StringComparison.Ordinal) is var i and >= 0 ? Value[..i] : null;

    /// <summary>The product part.</summary>
    public string Product => Value[(Value.IndexOf('/', StringComparison.Ordinal) + 1)..];

    /// <summary>Builds the raw key of a vendor and product as a source names them.</summary>
    /// <exception cref="ArgumentException">The product name has no letter or digit.</exception>
    public static ProductKey Create(string? vendor, string product) =>
        TryCreate(vendor, product, out var key)
            ? key
            : throw new ArgumentException($"'{product}' has no letter or digit to build a product key from.", nameof(product));

    /// <summary>Builds the raw key; <c>false</c> when the product name has no letter or digit.</summary>
    public static bool TryCreate(string? vendor, string? product, out ProductKey key)
    {
        var name = NormalizeProduct(product);
        if (name is null)
        {
            key = default;
            return false;
        }

        var owner = NormalizeVendor(vendor);
        key = new ProductKey(owner is null ? name : owner + "/" + name);
        return true;
    }

    /// <summary>
    /// Reads a stored key. Accepts catalogue keys (lower-case letters and digits, joined by single
    /// <c>-</c> or <c>.</c>, with an optional vendor part) and raw keys built by <see cref="Create"/>.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out ProductKey key)
    {
        key = default;
        if (string.IsNullOrEmpty(value) || value.Length > 200)
        {
            return false;
        }

        var parts = value.Split('/');
        if (parts.Length > 2 || !parts.All(IsKeyPart))
        {
            return false;
        }

        key = new ProductKey(value);
        return true;
    }

    /// <summary>The vendor part of a key: <c>null</c> for an empty, <c>n/a</c> or similar vendor.</summary>
    public static string? NormalizeVendor(string? vendor)
    {
        if (vendor is null || IgnoredVendors.Contains(vendor.Trim().ToLowerInvariant()))
        {
            return null;
        }

        var words = Words(vendor);
        if (words.Count > 1 && words[0] == "the")
        {
            words.RemoveAt(0);
        }

        var removed = true;
        while (removed && words.Count > 1)
        {
            removed = false;
            if (LegalForms.Contains(words[^1]))
            {
                words.RemoveAt(words.Count - 1);
                removed = true;
                continue;
            }

            removed = RemoveSuffix(words, VendorSuffixes);
        }

        return Join(words);
    }

    /// <summary>The product part of a key: <c>null</c> when the name has no letter or digit.</summary>
    public static string? NormalizeProduct(string? product)
    {
        if (product is null)
        {
            return null;
        }

        var words = Words(product);
        while (words.Count > 1 && RemoveSuffix(words, ProductSuffixes))
        {
        }

        return Join(words);
    }

    public override string ToString() => Value;

    /// <summary>Letters and digits without upper case, optionally joined by single <c>-</c> or <c>.</c>.</summary>
    private static bool IsKeyPart(string part)
    {
        var previousSeparator = true;
        foreach (var c in part)
        {
            var separator = c is '-' or '.';
            if (separator ? previousSeparator : !char.IsLetterOrDigit(c) || char.IsUpper(c))
            {
                return false;
            }

            previousSeparator = separator;
        }

        return !previousSeparator;
    }

    private static bool RemoveSuffix(List<string> words, string[][] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (words.Count > suffix.Length && words.TakeLast(suffix.Length).SequenceEqual(suffix, StringComparer.Ordinal))
            {
                words.RemoveRange(words.Count - suffix.Length, suffix.Length);
                return true;
            }
        }

        return false;
    }

    /// <summary>Lower-case words without diacritics; everything but letters and digits separates.</summary>
    private static List<string> Words(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                current.Append(char.ToLowerInvariant(c));
            }
            else if (c is '.' or '\'' or '’' && current.Length > 0)
            {
                // «Node.js», «O'Reilly»: punctuation inside a word joins rather than separates.
                continue;
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }

    private static string? Join(List<string> words)
    {
        var joined = string.Concat(words).Normalize(NormalizationForm.FormC);
        return joined.Length == 0 ? null : joined;
    }
}
