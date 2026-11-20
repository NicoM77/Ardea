using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ardea.CommentCheck;

internal static class DocText
{
    private static readonly HashSet<string> Opaque = new(["code", "list", "listheader", "item", "term", "description", "example"], StringComparer.Ordinal);

    public static IReadOnlyList<DocPart> Parts(SyntaxList<XmlNodeSyntax> content)
    {
        var raw = new List<DocPart>();
        Render(content, raw);

        var merged = new List<DocPart>();
        foreach (var part in raw)
        {
            if (merged.Count > 0 && (merged[^1].Literal is null) == (part.Literal is null))
            {
                merged[^1] = part.Literal is null ? merged[^1] : new DocPart(merged[^1].Literal + part.Literal);
                continue;
            }

            merged.Add(part);
        }

        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i].Literal is { } literal)
            {
                var collapsed = System.Text.RegularExpressions.Regex.Replace(literal, @"\s+", " ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
                collapsed = i == 0 ? collapsed.TrimStart() : collapsed;
                collapsed = i == merged.Count - 1 ? collapsed.TrimEnd() : collapsed;
                merged[i] = new DocPart(collapsed);
            }
        }

        return merged;
    }

    private static void Render(SyntaxList<XmlNodeSyntax> nodes, List<DocPart> parts)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case XmlTextSyntax text:
                    foreach (var token in text.TextTokens)
                    {
                        parts.Add(new DocPart(token.IsKind(SyntaxKind.XmlTextLiteralNewLineToken) ? " " : token.ValueText));
                    }

                    break;
                case XmlCDataSectionSyntax cdata:
                    parts.Add(new DocPart(string.Concat(cdata.TextTokens.Select(t => t.ValueText))));
                    break;
                case XmlElementSyntax { StartTag.Name.LocalName.ValueText: "c" } code:
                    parts.Add(new DocPart("`"));
                    Render(code.Content, parts);
                    parts.Add(new DocPart("`"));
                    break;
                case XmlElementSyntax element when Opaque.Contains(element.StartTag.Name.LocalName.ValueText):
                    parts.Add(new DocPart(null));
                    break;
                case XmlElementSyntax { StartTag.Name.LocalName.ValueText: "para" } paragraph:
                    parts.Add(new DocPart(" "));
                    Render(paragraph.Content, parts);
                    parts.Add(new DocPart(" "));
                    break;
                case XmlElementSyntax element:
                    Render(element.Content, parts);
                    break;
                case XmlEmptyElementSyntax:
                    parts.Add(new DocPart(null));
                    break;
            }
        }
    }
}
