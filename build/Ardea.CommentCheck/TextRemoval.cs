using System.Text;

namespace Ardea.CommentCheck;

internal static class TextRemoval
{
    public static string Apply(string text, IEnumerable<Range> removals)
    {
        var spans = removals
            .Select(r => Expand(text, r.Start.Value, r.End.Value))
            .OrderBy(s => s.Start)
            .ToList();
        var merged = new List<(int Start, int End, bool WholeLines)>();
        foreach (var span in spans)
        {
            if (merged.Count > 0 && span.Start <= merged[^1].End)
            {
                var last = merged[^1];
                merged[^1] = (last.Start, Math.Max(last.End, span.End), last.WholeLines && span.WholeLines);
                continue;
            }

            merged.Add(span);
        }

        var result = new StringBuilder(text.Length);
        var junctions = new List<int>();
        var position = 0;
        foreach (var (start, end, wholeLines) in merged)
        {
            result.Append(text, position, start - position);
            if (wholeLines)
            {
                junctions.Add(result.Length);
            }

            position = end;
        }

        result.Append(text, position, text.Length - position);
        var output = result.ToString();
        for (var j = junctions.Count - 1; j >= 0; j--)
        {
            output = Tidy(output, junctions[j]);
        }

        return output;
    }

    private static (int Start, int End, bool WholeLines) Expand(string text, int start, int end)
    {
        while (end > start && text[end - 1] is '\n' or '\r')
        {
            end--;
        }

        var lineStart = text.LastIndexOf('\n', Math.Max(start - 1, 0)) + 1;
        if (start == 0)
        {
            lineStart = 0;
        }

        var lineEnd = text.IndexOf('\n', end);
        lineEnd = lineEnd < 0 ? text.Length : lineEnd;
        var before = text[lineStart..start];
        var after = text[end..lineEnd];
        if (string.IsNullOrWhiteSpace(before) && string.IsNullOrWhiteSpace(after))
        {
            return (lineStart, Math.Min(lineEnd + 1, text.Length), true);
        }

        if (string.IsNullOrWhiteSpace(after))
        {
            var trimmed = start;
            while (trimmed > lineStart && text[trimmed - 1] is ' ' or '\t')
            {
                trimmed--;
            }

            return (trimmed, lineEnd, false);
        }

        var following = end;
        while (following < lineEnd && text[following] is ' ' or '\t' && (start == lineStart || text[start - 1] is ' ' or '\t' or '(' or '['))
        {
            following++;
        }

        return (start, following, false);
    }

    private static string Tidy(string text, int junction)
    {
        var lines = new List<string>(text.Split('\n'));
        var lineIndex = text.AsSpan(0, junction).Count('\n');
        bool Blank(int index) => index >= 0 && index < lines.Count && string.IsNullOrWhiteSpace(lines[index]);
        string Trimmed(int index) => index >= 0 && index < lines.Count ? lines[index].Trim() : string.Empty;

        var opens = lineIndex == 0 || Trimmed(lineIndex - 1).EndsWith('{') || Trimmed(lineIndex - 1).EndsWith('(') || Trimmed(lineIndex - 1).EndsWith('[');
        if (opens)
        {
            while (Blank(lineIndex) && lineIndex < lines.Count - 1)
            {
                lines.RemoveAt(lineIndex);
            }
        }

        var next = Trimmed(lineIndex);
        var closes = next.StartsWith('}') || next.StartsWith(')') || next.StartsWith(']');
        if (closes)
        {
            while (Blank(lineIndex - 1) && lineIndex - 1 > 0)
            {
                lines.RemoveAt(lineIndex - 1);
                lineIndex--;
            }
        }

        if (Blank(lineIndex - 1) && Blank(lineIndex) && lineIndex < lines.Count - 1)
        {
            lines.RemoveAt(lineIndex);
        }

        return string.Join('\n', lines);
    }
}
