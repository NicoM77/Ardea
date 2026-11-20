using System.Text.Json;
using Ardea.Assessment.Persistence;
using Ardea.Intel.Contracts;

namespace Ardea.Assessment.Features;

internal sealed record AnswersView(
    string Origin, WhatHappenedAnswer WhatHappened, AffectedAnswer Affected, ToDoAnswer ToDo, ModelAnswersView? Model = null, string? ModelNote = null);

internal sealed record ModelAnswersView(
    Guid AssessmentId,
    string Language,
    string? Model,
    string PromptVersion,
    DateTimeOffset CreatedAt,
    string WhatHappened,
    string Likelihood,
    string LikelihoodReason,
    string Confidence,
    string Impact,
    IReadOnlyList<ModelVerdictView> Verdicts,
    IReadOnlyList<ModelPreconditionView> Preconditions,
    IReadOnlyList<ModelStepView> Actions,
    IReadOnlyList<string> Unknowns,
    IReadOnlyList<ModelReasonView> Reasoning,
    IReadOnlyList<ModelSourceView> Sources,
    string? Provider,
    int RulesetVersion,
    int TokensIn,
    int TokensOut,
    int DurationMs,
    bool RequestedByPerson);

internal sealed record ModelVerdictView(ActionProjectRef Project, ActionComponentRef Component, string Verdict, bool Proposal);

internal sealed record ModelPreconditionView(string Condition, string State);

internal sealed record ModelStepView(string Text, IReadOnlyList<int> Sources, bool Own);

internal sealed record ModelReasonView(string Sentence, IReadOnlyList<int> Sources, IReadOnlyList<Guid> Components);

internal sealed record ModelSourceView(int Number, string? Name, string? Url);

internal sealed record WhatHappenedAnswer(string EntryKey, string? Title, string? Excerpt, string? Source, int MoreEntries);

internal sealed record AffectedAnswer(string Verdict, IReadOnlyList<AffectedItem> Items, int MoreItems);

internal sealed record AffectedItem(
    ActionProjectRef Project, ActionComponentRef Component, int Level, string? VersionVerdict, string? VersionReason, IReadOnlyList<AffectedRange> Ranges);

internal sealed record AffectedRange(string? Introduced, string? Fixed, string? LastAffected, string? Exact);

internal sealed record ToDoAnswer(string Kind, string? TargetVersion, string? InstalledVersion, IReadOnlyList<string> Steps, string? AdvisoryUrl);

internal static class AnswerTemplates
{
    public const int WhatHappenedLength = 240;
    public const int StepLength = 140;
    private const int MaxItems = 5;
    private const int MaxSteps = 3;
    private const int MaxRanges = 3;

    public static AnswersView For(
        IReadOnlyList<Finding> findings,
        IReadOnlyDictionary<Guid, EntryText> texts,
        Func<Finding, (ActionProjectRef Project, ActionComponentRef Component)> refs,
        string? targetVersion,
        IReadOnlyDictionary<Guid, AlertAssessment>? assessments = null)
    {
        var ordered = findings
            .OrderByDescending(f => f.Status == "open").ThenByDescending(f => f.Level).ThenBy(f => f.Id).ToList();
        var lead = ordered[0];
        var text = texts.GetValueOrDefault(lead.EntryId);
        var entries = findings.Select(f => f.EntryId).Distinct().Count();

        var what = new WhatHappenedAnswer(
            text?.Key ?? string.Empty, text?.Title, Cut(text?.Description, WhatHappenedLength), text?.Description is null ? null : text.DescriptionSource, entries - 1);

        var perComponent = ordered.Where(f => f.MatchType is "package" or "cpe" or "product")
            .GroupBy(f => f.ComponentId).Select(g => g.First()).ToList();
        var items = perComponent.Take(MaxItems).Select(f =>
        {
            var (project, component) = refs(f);
            return new AffectedItem(project, component, f.Level, f.VersionVerdict, f.VersionReason, Ranges(f));
        }).ToList();
        var verdict = perComponent.Exists(f => f.VersionVerdict == "in_range") ? "affected"
            : perComponent.Exists(f => f.VersionVerdict != "out_of_range") ? "unknown"
            : "not_affected";
        var affected = new AffectedAnswer(verdict, items, Math.Max(0, perComponent.Count - MaxItems));

        var installed = lead.Evidence.RootElement.TryGetProperty("installed", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
        var target = targetVersion ?? lead.FixedVersion;
        var steps = (text?.Solutions ?? []).Concat(text?.Workarounds ?? [])
            .Select(s => Cut(s, StepLength)!).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).Take(MaxSteps).ToList();
        var todo = target is not null
            ? new ToDoAnswer("update", target, installed, [], text?.AdvisoryUrl)
            : steps.Count > 0
                ? new ToDoAnswer("source_guidance", null, installed, steps, text?.AdvisoryUrl)
                : new ToDoAnswer("no_fix", null, installed, [], text?.AdvisoryUrl);
        var current = assessments?.GetValueOrDefault(lead.AlertId);
        if (current is null)
        {
            return new AnswersView("template", what, affected, todo);
        }

        if (current.Engine != AlertAssessment.ModelEngine || Estimating.StoredEstimate.Read(current.Output) is not { } estimate)
        {
            return new AnswersView("template", what, affected, todo, ModelNote: Estimating.FailedEstimate.WithoutModel);
        }

        var byComponent = findings.GroupBy(f => f.ComponentId).ToDictionary(g => g.Key, g => g.First());
        var model = new ModelAnswersView(
            current.Id, current.Language ?? string.Empty, current.Model, current.PromptVersion ?? string.Empty, current.CreatedAt,
            estimate.WhatHappened, Name(estimate.Likelihood.Level), estimate.Likelihood.Reason, Name(estimate.Confidence), estimate.Impact,
            [.. estimate.Verdicts.Where(v => byComponent.ContainsKey(v.ComponentId)).Select(v =>
            {
                var (project, component) = refs(byComponent[v.ComponentId]);
                return new ModelVerdictView(project, component, Name(v.Verdict), v.Verdict == Estimating.ModelVerdict.NotAffected);
            })],
            [.. estimate.Preconditions.Select(p => new ModelPreconditionView(p.Condition, Name(p.State)))],
            [.. estimate.Actions.Select(a => new ModelStepView(a.Step, a.Sources, a.Own))],
            estimate.Unknowns,
            [.. estimate.Reasoning.Select(r => new ModelReasonView(r.Sentence, r.Sources, r.Components))],
            [.. estimate.Sources.Select(s => new ModelSourceView(s.N, s.Name, s.Url))],
            current.Provider, current.RulesetVersion, current.TokensIn, current.TokensOut, current.DurationMs, current.RequestedBy is not null);
        return new AnswersView("model", what, affected, todo, model);
    }

    private static string Name<T>(T value)
        where T : struct, Enum => Estimating.StoredEstimate.Name(value);

    public static string? Cut(string? text, int length)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var flat = string.Join(' ', text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length <= length)
        {
            return flat;
        }

        for (var end = length - 1; end > length / 3; end--)
        {
            if (flat[end] is '.' or '!' or '?' && flat[end + 1] == ' ' && !IsAbbreviation(flat, end))
            {
                return flat[..(end + 1)];
            }
        }

        var space = flat.LastIndexOf(' ', length - 2);
        return (space > 0 ? flat[..space] : flat[..(length - 1)]).TrimEnd(',', ';', ':') + "…";
    }

    private static bool IsAbbreviation(string text, int dot) =>
        (dot > 0 && char.IsDigit(text[dot - 1]) && dot + 2 < text.Length && char.IsDigit(text[dot + 2]))
        || (dot >= 3 && text.AsSpan(dot - 3, 4) is "e.g." or "i.e." or "etc.")
        || (dot >= 2 && text.AsSpan(dot - 2, 3) is "vs.");

    private static IReadOnlyList<AffectedRange> Ranges(Finding finding)
    {
        if (!finding.Evidence.RootElement.TryGetProperty("version", out var version) || !version.TryGetProperty("statements", out var statements))
        {
            return [];
        }

        var all = new List<(AffectedRange Range, bool Deciding)>();
        foreach (var statement in statements.EnumerateArray())
        {
            var inRange = finding.VersionVerdict == "in_range" && statement.TryGetProperty("verdict", out var v) && v.GetString() == "in_range";
            foreach (var range in statement.GetProperty("ranges").EnumerateArray().Where(r => Text(r, "status") == "affected"))
            {
                var item = new AffectedRange(Text(range, "introduced"), Text(range, "fixed"), Text(range, "lastAffected"), Text(range, "exact"));
                all.Add((item, inRange && (finding.FixedVersion is null || item.Fixed == finding.FixedVersion)));
            }
        }

        var chosen = all.Exists(r => r.Deciding) ? all.Where(r => r.Deciding) : all;
        return [.. chosen.Select(r => r.Range).Distinct().Take(MaxRanges)];
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
