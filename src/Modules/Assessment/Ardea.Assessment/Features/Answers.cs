using System.Text.Json;
using Ardea.Assessment.Persistence;
using Ardea.Intel.Contracts;

namespace Ardea.Assessment.Features;

/// <summary>
/// The three answers that open an action, an alert and the overview's urgent actions (master plan 7.5,
/// 11.2): what happened, are we affected, what to do. <c>origin</c> is <c>template</c>: built from
/// structured data and the source's own words, without a language model (task 3.6). The parts are data,
/// not sentences, so the interface words them in the reader's language; only the source's text is
/// quoted as written.
/// </summary>
/// <param name="Origin"><c>template</c>; <c>model</c> when the alert behind the answers has a current assessment by the
/// language model: its texts are in <paramref name="Model"/>, the structured parts here stay as the data gives them.</param>
/// <param name="WhatHappened">What happened.</param>
/// <param name="Affected">Are we affected.</param>
/// <param name="ToDo">What to do.</param>
/// <param name="Model">The model's assessment (master plan 7.5), in the organisation's language; <c>null</c> without one.</param>
/// <param name="ModelNote"><c>without_model</c> when the model was asked and failed twice: the answers come from the rules
/// and templates alone («ohne KI-Einschätzung»).</param>
internal sealed record AnswersView(
    string Origin, WhatHappenedAnswer WhatHappened, AffectedAnswer Affected, ToDoAnswer ToDo, ModelAnswersView? Model = null, string? ModelNote = null);

/// <summary>The assessment by the language model behind the answers (master plan 7.5, table «Ausgabe als JSON»).</summary>
/// <param name="AssessmentId">The stored assessment.</param>
/// <param name="Language">The language of its texts.</param>
/// <param name="Model">The model that wrote it.</param>
/// <param name="PromptVersion">The prompt's version.</param>
/// <param name="CreatedAt">When it was made.</param>
/// <param name="WhatHappened">One or two sentences, at most 240 characters.</param>
/// <param name="Likelihood">Of a successful attack here: <c>high</c>, <c>medium</c>, <c>low</c>.</param>
/// <param name="LikelihoodReason">One sentence.</param>
/// <param name="Confidence">Of the assessment: <c>high</c>, <c>medium</c>, <c>low</c>.</param>
/// <param name="Impact">One sentence, at most 120 characters.</param>
/// <param name="Verdicts">Per hit component.</param>
/// <param name="Preconditions">The conditions an attack needs and whether they hold here.</param>
/// <param name="Actions">Ordered steps, each with its sources or marked as the model's own.</param>
/// <param name="Unknowns">What the profile does not tell.</param>
/// <param name="Reasoning">Sentences with the numbers of their sources.</param>
/// <param name="Sources">The numbered sources the numbers refer to.</param>
/// <param name="Provider">The configured provider that answered, e.g. <c>local</c>.</param>
/// <param name="RulesetVersion">The rule set the assessment was made with.</param>
/// <param name="TokensIn">Tokens sent.</param>
/// <param name="TokensOut">Tokens received.</param>
/// <param name="DurationMs">How long the calls took.</param>
/// <param name="RequestedByPerson">A person asked for it; otherwise the system did, within the action's quota.</param>
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

/// <summary>The model's verdict on one component.</summary>
/// <param name="Project">The component's project.</param>
/// <param name="Component">The component.</param>
/// <param name="Verdict"><c>affected</c>, <c>likely</c>, <c>uncertain</c>, <c>unlikely</c> or <c>not_affected</c>.</param>
/// <param name="Proposal">The verdict is «probably not affected»: a proposal a person confirms through the finding's review, which closes it as
/// not affected; until then it counts like <c>unlikely</c> (master plan 7.5).</param>
internal sealed record ModelVerdictView(ActionProjectRef Project, ActionComponentRef Component, string Verdict, bool Proposal);

/// <param name="Condition">What an attack needs.</param>
/// <param name="State"><c>met</c>, <c>not_met</c> or <c>unknown</c>.</param>
internal sealed record ModelPreconditionView(string Condition, string State);

/// <param name="Text">At most 140 characters.</param>
/// <param name="Sources">Numbers of <see cref="ModelAnswersView.Sources"/>.</param>
/// <param name="Own">The model's own recommendation, without a source.</param>
internal sealed record ModelStepView(string Text, IReadOnlyList<int> Sources, bool Own);

internal sealed record ModelReasonView(string Sentence, IReadOnlyList<int> Sources, IReadOnlyList<Guid> Components);

/// <param name="Number">The number the texts refer to.</param>
/// <param name="Name">The source of the record, for number 1.</param>
/// <param name="Url">The reference's address.</param>
internal sealed record ModelSourceView(int Number, string? Name, string? Url);

/// <summary>What happened: the entry's title and the first sentences of its description, in the source's words.</summary>
/// <param name="EntryKey">The entry the answer is about: for an action, its open finding of the highest level.</param>
/// <param name="Title">The entry's title.</param>
/// <param name="Excerpt">The description cut at a sentence end to at most 240 characters; a quotation, not Ardea's words.</param>
/// <param name="Source">Whose words the excerpt is, e.g. <c>CVE Program</c>.</param>
/// <param name="MoreEntries">How many further entries the action covers.</param>
internal sealed record WhatHappenedAnswer(string EntryKey, string? Title, string? Excerpt, string? Source, int MoreEntries);

/// <summary>Are we affected: per component, the installed version against the ranges that decided.</summary>
/// <param name="Verdict"><c>affected</c> when a version lies in an affected range, <c>unknown</c> when one could not be checked, else <c>not_affected</c>.</param>
/// <param name="Items">The components, highest level first, at most five.</param>
/// <param name="MoreItems">How many components are left out.</param>
internal sealed record AffectedAnswer(string Verdict, IReadOnlyList<AffectedItem> Items, int MoreItems);

/// <param name="Project">The component's project.</param>
/// <param name="Component">The component.</param>
/// <param name="Level">The level of its finding.</param>
/// <param name="VersionVerdict"><c>in_range</c>, <c>out_of_range</c> or <c>unknown</c>; <c>null</c> for a weak match.</param>
/// <param name="VersionReason">Why, e.g. <c>no_installed_version</c>, <c>distribution_package</c>.</param>
/// <param name="Ranges">The affected ranges the record states for the product: the one the version lies in, or all of them (at most three) when it lies in none.</param>
internal sealed record AffectedItem(
    ActionProjectRef Project, ActionComponentRef Component, int Level, string? VersionVerdict, string? VersionReason, IReadOnlyList<AffectedRange> Ranges);

/// <summary>A range as the record states it: from <c>introduced</c> to before <c>fixed</c>, up to <c>lastAffected</c>, or one <c>exact</c> version.</summary>
internal sealed record AffectedRange(string? Introduced, string? Fixed, string? LastAffected, string? Exact);

/// <summary>What to do (master plan 7.4): the target version, else the source's solutions and workarounds, else no fix named.</summary>
/// <param name="Kind"><c>update</c>, <c>source_guidance</c> or <c>no_fix</c>.</param>
/// <param name="TargetVersion">For <c>update</c>: the fix version of the range the installed version lies in, or the highest named fix.</param>
/// <param name="InstalledVersion">The version installed now, for «from … to …».</param>
/// <param name="Steps">For <c>source_guidance</c>: solutions, then workarounds, as the source wrote them, each cut to 140 characters; at most three.</param>
/// <param name="AdvisoryUrl">The advisory to read, always when one is known.</param>
internal sealed record ToDoAnswer(string Kind, string? TargetVersion, string? InstalledVersion, IReadOnlyList<string> Steps, string? AdvisoryUrl);

/// <summary>Builds the answers from findings, the entries' texts and the components (master plan 7.5, table «Ohne Sprachmodell»).</summary>
internal static class AnswerTemplates
{
    public const int WhatHappenedLength = 240;
    public const int StepLength = 140;
    private const int MaxItems = 5;
    private const int MaxSteps = 3;
    private const int MaxRanges = 3;

    /// <summary>
    /// The answers for a set of findings: an action's, or an alert's. The entry, the version and the target
    /// come from the open finding of the highest level; without open findings, from the highest of all.
    /// </summary>
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

        // One item per component: its open finding of the highest level decides.
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
        // The lead alert's current assessment, if any (phase 4): the model's texts, or the note that it failed.
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

    /// <summary>
    /// A source text cut to at most <paramref name="length"/> characters at the end of a sentence (master plan
    /// 7.5, «Längen»); a text without a sentence end in reach is cut at a word and marked with an ellipsis.
    /// </summary>
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

    /// <summary>A period that ends a version or an abbreviation such as «e.g.» does not end a sentence.</summary>
    private static bool IsAbbreviation(string text, int dot) =>
        (dot > 0 && char.IsDigit(text[dot - 1]) && dot + 2 < text.Length && char.IsDigit(text[dot + 2]))
        || (dot >= 3 && text.AsSpan(dot - 3, 4) is "e.g." or "i.e." or "etc.")
        || (dot >= 2 && text.AsSpan(dot - 2, 3) is "vs.");

    /// <summary>The affected ranges from the finding's version check: those containing the version, else all.</summary>
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
                // Of a statement that put the version in range, the range with the finding's fix is the one it lies in.
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
