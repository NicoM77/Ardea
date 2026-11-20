using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Ardea.CommentCheck;

internal static partial class CSharpComments
{
    private static readonly CSharpParseOptions Options = new(LanguageVersion.Preview, DocumentationMode.Parse);

    public static List<Finding> Find(string path, string text, OpenApiTexts openApi)
    {
        var tree = CSharpSyntaxTree.ParseText(text, Options);
        var root = tree.GetRoot();
        var lines = tree.GetText().Lines;
        var trivia = root.DescendantTrivia(descendIntoTrivia: true).ToList();
        var pragmaLines = trivia
            .Where(t => t.GetStructure() is PragmaWarningDirectiveTriviaSyntax pragma && pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword))
            .Select(t => lines.GetLineFromPosition(t.SpanStart).LineNumber)
            .ToHashSet();
        var commentLines = trivia
            .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) && IsAloneOnLine(t, lines))
            .Select(t => lines.GetLineFromPosition(t.SpanStart).LineNumber)
            .ToHashSet();

        var findings = new List<Finding>();
        foreach (var comment in trivia)
        {
            switch (comment.Kind())
            {
                case SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia:
                    var line = lines.GetLineFromPosition(comment.SpanStart).LineNumber;
                    if (!ToolDirective().IsMatch(comment.ToString()) && !Justifies(line, pragmaLines, commentLines))
                    {
                        findings.Add(new Finding(path, line + 1, "comment: " + Excerpt(comment.ToString()), Range(comment.Span)));
                    }

                    break;
                case SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia:
                    findings.AddRange(Documentation(path, comment, lines, openApi));
                    break;
            }
        }

        findings.AddRange(SqlComments.Find(path, root, lines));
        return findings;
    }

    private static IEnumerable<Finding> Documentation(string path, SyntaxTrivia comment, TextLineCollection lines, OpenApiTexts openApi)
    {
        var structure = (DocumentationCommentTriviaSyntax)comment.GetStructure()!;
        var unused = new List<XmlNodeSyntax>();
        var used = 0;
        foreach (var node in structure.Content)
        {
            switch (node)
            {
                case XmlTextSyntax text when string.IsNullOrWhiteSpace(string.Concat(text.TextTokens.Select(t => t.ValueText))):
                    break;
                case XmlEmptyElementSyntax { Name.LocalName.ValueText: "inheritdoc" } when Scope.IsMigration(path):
                    used++;
                    break;
                case XmlElementSyntax element when openApi.Contains(DocText.Parts(element.Content)):
                    used++;
                    break;
                default:
                    unused.Add(node);
                    break;
            }
        }

        if (unused.Count == 0)
        {
            yield break;
        }

        if (used == 0)
        {
            yield return new Finding(
                path,
                lines.GetLineFromPosition(comment.FullSpan.Start).LineNumber + 1,
                "XML documentation the OpenAPI document does not use: " + Excerpt(comment.ToString()),
                Range(comment.FullSpan));
            yield break;
        }

        foreach (var node in unused)
        {
            var first = lines.GetLineFromPosition(node.SpanStart);
            var last = lines.GetLineFromPosition(node.Span.End);
            var sharesLine = structure.Content.Any(other => other != node
                && other is not XmlTextSyntax
                && lines.GetLineFromPosition(other.Span.End).LineNumber >= first.LineNumber
                && lines.GetLineFromPosition(other.SpanStart).LineNumber <= last.LineNumber);
            var removal = sharesLine ? Range(node.Span) : new Range(first.Start, last.EndIncludingLineBreak);
            yield return new Finding(path, first.LineNumber + 1, "XML documentation the OpenAPI document does not use: " + Excerpt(node.ToString()), removal);
        }
    }

    private static bool Justifies(int line, HashSet<int> pragmaLines, HashSet<int> commentLines)
    {
        if (pragmaLines.Contains(line))
        {
            return true;
        }

        if (!commentLines.Contains(line))
        {
            return false;
        }

        var next = line + 1;
        while (commentLines.Contains(next))
        {
            next++;
        }

        return pragmaLines.Contains(next);
    }

    private static bool IsAloneOnLine(SyntaxTrivia trivia, TextLineCollection lines)
    {
        var line = lines.GetLineFromPosition(trivia.SpanStart);
        return string.IsNullOrWhiteSpace(line.Text!.ToString(TextSpan.FromBounds(line.Start, trivia.SpanStart)));
    }

    private static Range Range(TextSpan span) => new(span.Start, span.End);

    internal static string Excerpt(string comment)
    {
        var text = OpenApiTexts.Collapse(comment);
        return text.Length <= 80 ? text : text[..77] + "...";
    }

    [GeneratedRegex(@"^//\s*(<auto-generated|ReSharper (disable|restore) |language=)", RegexOptions.CultureInvariant)]
    private static partial Regex ToolDirective();
}
