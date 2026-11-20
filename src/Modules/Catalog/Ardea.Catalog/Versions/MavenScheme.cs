using System.Diagnostics.CodeAnalysis;
using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Versions;

internal sealed class MavenScheme : IVersionScheme
{
    public string Name => VersionSchemeNames.Maven;

    public bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out ParsedVersion? version)
    {
        version = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !char.IsAsciiDigit(trimmed[0])
            || !trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+'))
        {
            return false;
        }

        var items = Parse(trimmed);
        version = new MavenVersion(trimmed, items.ToString(), items);
        return true;
    }

    private static ListItem Parse(string text)
    {
        var version = text.ToLowerInvariant();
        var root = new ListItem();
        var list = root;
        var stack = new Stack<ListItem>();
        stack.Push(list);
        var isDigit = false;
        var start = 0;
        for (var i = 0; i < version.Length; i++)
        {
            var c = version[i];
            if (c == '.')
            {
                list.Add(i == start ? IntItem.Zero : ParseItem(isDigit, version[start..i]));
                start = i + 1;
            }
            else if (c == '-')
            {
                list.Add(i == start ? IntItem.Zero : ParseItem(isDigit, version[start..i]));
                start = i + 1;
                list = NewSubList(list, stack);
            }
            else if (char.IsAsciiDigit(c))
            {
                if (!isDigit && i > start)
                {
                    list.Add(new StringItem(version[start..i], followedByDigit: true));
                    start = i;
                    list = NewSubList(list, stack);
                }

                isDigit = true;
            }
            else
            {
                if (isDigit && i > start)
                {
                    list.Add(ParseItem(true, version[start..i]));
                    start = i;
                    list = NewSubList(list, stack);
                }

                isDigit = false;
            }
        }

        if (version.Length > start)
        {
            list.Add(ParseItem(isDigit, version[start..]));
        }

        while (stack.Count > 0)
        {
            stack.Pop().Normalize();
        }

        return root;
    }

    private static ListItem NewSubList(ListItem parent, Stack<ListItem> stack)
    {
        var sub = new ListItem();
        parent.Add(sub);
        stack.Push(sub);
        return sub;
    }

    private static Item ParseItem(bool isDigit, string text) =>
        isDigit ? new IntItem(NumericText.Trim(text)) : new StringItem(text, followedByDigit: false);

    private sealed class MavenVersion(string original, string normalized, ListItem items)
        : ParsedVersion(VersionSchemeNames.Maven, original, normalized)
    {
        private ListItem Items => items;

        protected override int CompareToSameScheme(ParsedVersion other) => items.CompareTo(((MavenVersion)other).Items);
    }

    private abstract class Item
    {
        public abstract bool IsNull { get; }

        public abstract int CompareTo(Item? other);
    }

    private sealed class IntItem(string digits) : Item
    {
        public static readonly IntItem Zero = new("0");

        public string Digits => digits;

        public override bool IsNull => digits == "0";

        public override int CompareTo(Item? other) => other switch
        {
            null => IsNull ? 0 : 1,
            IntItem i => NumericText.Compare(digits, i.Digits),
            _ => 1,
        };

        public override string ToString() => digits;
    }

    private sealed class StringItem : Item
    {
        private static readonly string[] Qualifiers = ["alpha", "beta", "milestone", "rc", "snapshot", "", "sp"];

        private static readonly string ReleaseIndex = Array.IndexOf(Qualifiers, string.Empty).ToString(System.Globalization.CultureInfo.InvariantCulture);

        public StringItem(string value, bool followedByDigit)
        {
            if (followedByDigit && value.Length == 1)
            {
                value = value[0] switch
                {
                    'a' => "alpha",
                    'b' => "beta",
                    'm' => "milestone",
                    _ => value,
                };
            }

            Value = value switch
            {
                "ga" or "final" or "release" => string.Empty,
                "cr" => "rc",
                _ => value,
            };
        }

        public string Value { get; }

        public override bool IsNull => string.CompareOrdinal(Comparable(Value), ReleaseIndex) == 0;

        public override int CompareTo(Item? other) => other switch
        {
            null => Math.Sign(string.CompareOrdinal(Comparable(Value), ReleaseIndex)),
            StringItem s => Math.Sign(string.CompareOrdinal(Comparable(Value), Comparable(s.Value))),
            _ => -1,
        };

        public override string ToString() => Value;

        private static string Comparable(string qualifier)
        {
            var index = Array.IndexOf(Qualifiers, qualifier);
            return index >= 0
                ? index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : Qualifiers.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + qualifier;
        }
    }

    private sealed class ListItem : Item
    {
        private readonly List<Item> _items = [];

        public override bool IsNull => _items.Count == 0;

        public void Add(Item item) => _items.Add(item);

        public void Normalize()
        {
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].IsNull)
                {
                    _items.RemoveAt(i);
                }
                else if (_items[i] is not ListItem)
                {
                    break;
                }
            }
        }

        public override int CompareTo(Item? other)
        {
            switch (other)
            {
                case null:
                    foreach (var item in _items)
                    {
                        var result = item.CompareTo(null);
                        if (result != 0)
                        {
                            return result;
                        }
                    }

                    return 0;
                case IntItem:
                    return -1;
                case StringItem:
                    return 1;
            }

            var list = (ListItem)other;
            for (var i = 0; i < Math.Max(_items.Count, list._items.Count); i++)
            {
                var left = i < _items.Count ? _items[i] : null;
                var right = i < list._items.Count ? list._items[i] : null;
                var c = left is null ? (right is null ? 0 : -right.CompareTo(null)) : left.CompareTo(right);
                if (c != 0)
                {
                    return c;
                }
            }

            return 0;
        }

        public override string ToString()
        {
            var builder = new System.Text.StringBuilder();
            foreach (var item in _items)
            {
                if (builder.Length > 0)
                {
                    builder.Append(item is ListItem ? '-' : '.');
                }

                builder.Append(item);
            }

            return builder.ToString();
        }
    }
}
