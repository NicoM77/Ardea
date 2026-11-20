using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ardea.CommentCheck;

internal sealed partial class OpenApiTexts
{
    private readonly HashSet<string> _texts;

    private OpenApiTexts(HashSet<string> texts) => _texts = texts;

    public static OpenApiTexts Load(string root)
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        var directory = Path.Combine(root, "openapi");
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                Collect(document.RootElement, texts);
            }
        }

        return new OpenApiTexts(texts);
    }

    public static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();

    public bool Contains(IReadOnlyList<DocPart> parts)
    {
        if (parts.All(p => p.Literal is not null))
        {
            return _texts.Contains(string.Concat(parts.Select(p => p.Literal)));
        }

        var pattern = new Regex(
            "^" + string.Concat(parts.Select(p => p.Literal is null ? ".*?" : Regex.Escape(p.Literal))) + "$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));
        return _texts.Any(pattern.IsMatch);
    }

    private static void Collect(JsonElement element, HashSet<string> texts)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, texts);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, texts);
                }

                break;
            case JsonValueKind.String:
                texts.Add(Collapse(element.GetString()!));
                break;
        }
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}

internal sealed record DocPart(string? Literal);
