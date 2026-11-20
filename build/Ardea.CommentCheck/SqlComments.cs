using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Ardea.CommentCheck;

internal static partial class SqlComments
{
    public static IEnumerable<Finding> Find(string path, SyntaxNode root, TextLineCollection lines)
    {
        var text = root.ToFullString();
        foreach (var literal in root.DescendantNodes().Where(n => n is LiteralExpressionSyntax or InterpolatedStringExpressionSyntax))
        {
            if (!IsSql(literal))
            {
                continue;
            }

            foreach (var (start, end) in Content(literal))
            {
                foreach (var comment in Lex(text, start, end))
                {
                    yield return new Finding(
                        path,
                        lines.GetLineFromPosition(comment.Start.Value).LineNumber + 1,
                        "SQL comment: " + CSharpComments.Excerpt(text[comment]),
                        comment);
                }
            }
        }
    }

    private static bool IsSql(SyntaxNode literal)
    {
        if (literal is LiteralExpressionSyntax plain && !plain.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return false;
        }

        if (literal.Parent is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax invocation }
            && invocation.Expression.ToString().Contains("Sql", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var content = literal.ToString().TrimStart('$', '@', '"').TrimStart();
        return Statement().IsMatch(content);
    }

    private static IEnumerable<(int Start, int End)> Content(SyntaxNode literal)
    {
        switch (literal)
        {
            case LiteralExpressionSyntax { Token: var token } when token.IsKind(SyntaxKind.StringLiteralToken) && token.Text.StartsWith('@'):
                yield return (token.SpanStart + 2, token.Span.End - 1);
                break;
            case LiteralExpressionSyntax { Token: var token } when token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken) || token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken):
                var quotes = token.Text.TakeWhile(c => c == '"').Count();
                yield return (token.SpanStart + quotes, token.Span.End - quotes);
                break;
            case InterpolatedStringExpressionSyntax interpolated when interpolated.StringStartToken.Text.Contains("\"\"\"", StringComparison.Ordinal) || interpolated.StringStartToken.Text.Contains('@', StringComparison.Ordinal):
                foreach (var part in interpolated.Contents.OfType<InterpolatedStringTextSyntax>())
                {
                    yield return (part.SpanStart, part.Span.End);
                }

                break;
        }
    }

    private static IEnumerable<Range> Lex(string text, int start, int end)
    {
        var i = start;
        while (i < end)
        {
            var c = text[i];
            var next = i + 1 < end ? text[i + 1] : '\0';
            if (c is '\'' or '"')
            {
                i = text.IndexOf(c, i + 1);
                i = i < 0 || i >= end ? end : i + 1;
            }
            else if (c == '-' && next == '-')
            {
                var stop = text.IndexOf('\n', i);
                stop = stop < 0 || stop > end ? end : stop;
                yield return new Range(i, stop);
                i = stop;
            }
            else if (c == '/' && next == '*')
            {
                var stop = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                stop = stop < 0 || stop + 2 > end ? end : stop + 2;
                yield return new Range(i, stop);
                i = stop;
            }
            else
            {
                i++;
            }
        }
    }

    [GeneratedRegex(@"^(--|/\*|(SELECT|INSERT|UPDATE|DELETE|CREATE|ALTER|DROP|WITH|DO|GRANT|REVOKE|COMMENT|SET|BEGIN|TRUNCATE)\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Statement();
}
