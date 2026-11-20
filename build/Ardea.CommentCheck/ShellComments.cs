using System.Text.RegularExpressions;

namespace Ardea.CommentCheck;

internal static partial class ShellComments
{
    public static List<Finding> Find(string path, string text)
    {
        var findings = new List<Finding>();
        var heredocs = new Queue<(string Word, bool StripTabs)>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            switch (c)
            {
                case '\\':
                    i += 2;
                    break;
                case '\'':
                case '`':
                    i = Skip(text, i + 1, c);
                    break;
                case '"':
                    i = SkipDoubleQuoted(text, i + 1);
                    break;
                case '<' when At(text, i, "<<") && !At(text, i, "<<<"):
                    i = ReadHeredoc(text, i + 2, heredocs);
                    break;
                case '\n':
                    i = SkipHeredocBodies(text, i + 1, heredocs);
                    break;
                case '#' when StartsWord(text, i):
                    var end = text.IndexOf('\n', i);
                    end = end < 0 ? text.Length : end;
                    var comment = text[i..end];
                    if (!(i == 0 && comment.StartsWith("#!", StringComparison.Ordinal)) && !Directive().IsMatch(comment))
                    {
                        findings.Add(new Finding(path, LineOf(text, i), "comment: " + CSharpComments.Excerpt(comment), new Range(i, end)));
                    }

                    i = end;
                    break;
                default:
                    i++;
                    break;
            }
        }

        return findings;
    }

    private static bool At(string text, int i, string value) => string.CompareOrdinal(text, i, value, 0, value.Length) == 0;

    private static bool StartsWord(string text, int i) => i == 0 || char.IsWhiteSpace(text[i - 1]) || text[i - 1] is ';' or '&' or '|' or '(' or ')';

    private static int Skip(string text, int i, char quote)
    {
        var end = text.IndexOf(quote, i);
        return end < 0 ? text.Length : end + 1;
    }

    private static int SkipDoubleQuoted(string text, int i)
    {
        while (i < text.Length && text[i] != '"')
        {
            i += text[i] == '\\' ? 2 : 1;
        }

        return i + 1;
    }

    private static int ReadHeredoc(string text, int i, Queue<(string Word, bool StripTabs)> heredocs)
    {
        var stripTabs = i < text.Length && text[i] == '-';
        i += stripTabs ? 1 : 0;
        while (i < text.Length && text[i] is ' ' or '\t')
        {
            i++;
        }

        var start = i;
        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ';' and not '|' and not '&' and not ')' and not '<' and not '>')
        {
            i++;
        }

        var word = text[start..i].Trim('\'', '"').Replace("\\", string.Empty, StringComparison.Ordinal);
        if (word.Length > 0)
        {
            heredocs.Enqueue((word, stripTabs));
        }

        return i;
    }

    private static int SkipHeredocBodies(string text, int i, Queue<(string Word, bool StripTabs)> heredocs)
    {
        while (heredocs.TryDequeue(out var heredoc))
        {
            while (i < text.Length)
            {
                var end = text.IndexOf('\n', i);
                end = end < 0 ? text.Length : end;
                var line = text[i..end];
                i = Math.Min(end + 1, text.Length);
                if ((heredoc.StripTabs ? line.TrimStart('\t') : line) == heredoc.Word)
                {
                    break;
                }
            }
        }

        return i;
    }

    private static int LineOf(string text, int position) => text.AsSpan(0, position).Count('\n') + 1;

    [GeneratedRegex(@"^#\s*shellcheck\s", RegexOptions.CultureInvariant)]
    private static partial Regex Directive();
}
