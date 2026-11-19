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
/// <param name="Origin"><c>template</c>; <c>model</c> once an assessment exists (phase 4).</param>
/// <param name="WhatHappened">What happened.</param>
/// <param name="Affected">Are we affected.</param>
/// <param name="ToDo">What to do.</param>
internal sealed record AnswersView(string Origin, WhatHappenedAnswer WhatHappened, AffectedAnswer Affected, ToDoAnswer ToDo);

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
        string? targetVersion)
    {
        var ordered = findings
            .OrderByDescending(f => f.Status is "open" or "in_review").ThenByDescending(f => f.Level).ThenBy(f => f.Id).ToList();
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
        return new AnswersView("template", what, affected, todo);
    }

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
