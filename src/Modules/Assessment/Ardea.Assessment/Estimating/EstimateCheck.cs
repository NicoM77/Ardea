using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ardea.Assessment.Estimating;

internal static partial class EstimateCheck
{
    public const int WhatHappenedLength = 240;
    public const int StepLength = 140;
    public const int ImpactLength = 120;
    public const int SentenceLength = 300;
    public const int MaxActions = 5;
    public const int MaxListItems = 8;

    public static (StoredEstimate? Estimate, string? Problem) Check(string? text, EstimateInput input)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, "empty");
        }

        var json = StripBidi(Unfence(text));
        EstimateAnswer? answer;
        try
        {
            using (var document = JsonDocument.Parse(json))
            {
                if (!ExactValues(document.RootElement))
                {
                    return (null, "schema");
                }
            }

            answer = JsonSerializer.Deserialize<EstimateAnswer>(json, StoredEstimate.Json);
        }
        catch (JsonException)
        {
            return (null, "schema");
        }

        if (answer is null)
        {
            return (null, "schema");
        }

        var problem = Problem(answer, input);
        if (problem is not null)
        {
            return (null, problem);
        }

        return (new StoredEstimate(
            [.. answer.Verdicts.Select(v => new StoredVerdict(input.ComponentIds[v.Component], v.Verdict, input.ComponentContexts.GetValueOrDefault(v.Component, string.Empty)))],
            answer.Likelihood with { Reason = answer.Likelihood.Reason.Trim() },
            answer.Confidence,
            answer.WhatHappened.Trim(),
            [.. answer.Preconditions.Select(p => p with { Condition = p.Condition.Trim() })],
            answer.Impact.Trim(),
            [.. answer.Actions.Select(a => a with { Step = a.Step.Trim(), Sources = [.. a.Sources.Distinct().Order()] })],
            [.. answer.Unknowns.Select(u => u.Trim())],
            [.. answer.Reasoning.Select(r => new StoredReasoning(r.Sentence.Trim(), [.. r.Sources.Distinct().Order()], [.. r.Components.Distinct().Select(c => input.ComponentIds[c])]))],
            input.Sources), null);
    }

    private static bool ExactValues(JsonElement root)
    {
        static bool Exact<T>(JsonElement parent, string property)
            where T : struct, Enum =>
            parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value)
            || (value.ValueKind == JsonValueKind.String && Enum.GetValues<T>().Select(StoredEstimate.Name).Contains(value.GetString(), StringComparer.Ordinal));

        static IEnumerable<JsonElement> Items(JsonElement parent, string property) =>
            parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray()
                : [];

        return Exact<ModelGrade>(root, "confidence")
            && (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("likelihood", out var likelihood) || Exact<ModelGrade>(likelihood, "level"))
            && Items(root, "verdicts").All(v => Exact<ModelVerdict>(v, "verdict"))
            && Items(root, "preconditions").All(p => Exact<ConditionState>(p, "state"));
    }

    private static string StripBidi(string text) => text.Any(IsBidi) ? new string([.. text.Where(c => !IsBidi(c))]) : text;

    private static bool IsBidi(char c) => c is '؜' or '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');

    private static string? Problem(EstimateAnswer answer, EstimateInput input)
    {
        if (answer.Verdicts is null || answer.Likelihood is null || answer.Preconditions is null || answer.Actions is null
            || answer.Unknowns is null || answer.Reasoning is null
            || answer.Verdicts.Any(v => v is null) || answer.Preconditions.Any(p => p is null) || answer.Actions.Any(a => a is null || a.Sources is null)
            || answer.Unknowns.Any(u => u is null) || answer.Reasoning.Any(r => r is null || r.Sources is null || r.Components is null))
        {
            return "schema";
        }

        if (!Enum.IsDefined(answer.Confidence) || !Enum.IsDefined(answer.Likelihood.Level)
            || answer.Verdicts.Any(v => !Enum.IsDefined(v.Verdict)) || answer.Preconditions.Any(p => !Enum.IsDefined(p.State)))
        {
            return "schema";
        }

        if (answer.Verdicts.Any(v => !input.ComponentIds.ContainsKey(v.Component))
            || answer.Reasoning.Any(r => r.Components.Any(c => !input.ComponentIds.ContainsKey(c))))
        {
            return "unknown_component";
        }

        if (answer.Verdicts.Count != input.ComponentIds.Count || answer.Verdicts.Select(v => v.Component).Distinct(StringComparer.Ordinal).Count() != answer.Verdicts.Count)
        {
            return "verdict_per_component";
        }

        var sources = input.Sources.Select(s => s.N).ToHashSet();
        if (answer.Actions.Any(a => a.Sources.Any(n => !sources.Contains(n))) || answer.Reasoning.Any(r => r.Sources.Any(n => !sources.Contains(n))))
        {
            return "unknown_source";
        }

        if (answer.Actions.Count is 0 or > MaxActions || answer.Reasoning.Count is 0 or > MaxListItems
            || answer.Preconditions.Count > MaxListItems || answer.Unknowns.Count > MaxListItems)
        {
            return "count";
        }

        if (answer.Actions.Any(a => a.Sources.Count == 0 && !a.Own) || answer.Reasoning.Any(r => r.Sources.Count == 0 && r.Components.Count == 0))
        {
            return "unreferenced";
        }

        if (Blank(answer.WhatHappened) || Blank(answer.Impact) || Blank(answer.Likelihood.Reason)
            || answer.Actions.Any(a => Blank(a.Step)) || answer.Reasoning.Any(r => Blank(r.Sentence))
            || answer.Preconditions.Any(p => Blank(p.Condition)) || answer.Unknowns.Any(Blank))
        {
            return "empty_text";
        }

        if (answer.WhatHappened.Trim().Length > WhatHappenedLength || answer.Impact.Trim().Length > ImpactLength
            || answer.Actions.Any(a => a.Step.Trim().Length > StepLength)
            || Texts(answer).Any(t => t.Trim().Length > SentenceLength))
        {
            return "length";
        }

        if (Texts(answer).Any(t => Address().IsMatch(t) || t.Any(c => char.IsControl(c) && c is not '\n')))
        {
            return "address_in_text";
        }

        return null;
    }

    private static IEnumerable<string> Texts(EstimateAnswer answer) =>
        new[] { answer.WhatHappened, answer.Impact, answer.Likelihood.Reason }
            .Concat(answer.Actions.Select(a => a.Step))
            .Concat(answer.Reasoning.Select(r => r.Sentence))
            .Concat(answer.Preconditions.Select(p => p.Condition))
            .Concat(answer.Unknowns);

    private static bool Blank(string? text) => string.IsNullOrWhiteSpace(text);

    private static string Unfence(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var start = trimmed.IndexOf('\n', StringComparison.Ordinal);
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (start > 0 && end > start)
            {
                return trimmed[(start + 1)..end];
            }
        }

        return trimmed;
    }

    [GeneratedRegex(@"[a-z][a-z0-9+.-]*://|\bwww\.|<\s*/?\s*[a-z]|@[a-z0-9-]+\.[a-z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Address();
}
