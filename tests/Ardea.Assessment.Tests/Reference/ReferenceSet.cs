using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ardea.Assessment.Tests.Reference;

internal sealed record ReferenceSet(IReadOnlyList<ReferencePair> Pairs, IReadOnlyDictionary<string, string> Records)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Folder
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Ardea.slnx")))
                {
                    return Path.Combine(dir.FullName, "data", "reference");
                }
            }

            throw new DirectoryNotFoundException("No Ardea.slnx above the test binaries.");
        }
    }

    public static ReferenceSet Load(string folder)
    {
        var file = JsonSerializer.Deserialize<PairFile>(File.ReadAllText(Path.Combine(folder, "pairs.json")), Json)!;
        var records = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(folder, "records.jsonl")).Where(l => l.Length > 0))
        {
            using var record = JsonDocument.Parse(line);
            records[record.RootElement.GetProperty("cveMetadata").GetProperty("cveId").GetString()!] = line;
        }

        return new ReferenceSet(file.Pairs, records);
    }

    private sealed record PairFile(string Format, int Version, IReadOnlyList<ReferencePair> Pairs);
}

internal sealed record ReferencePair(
    string Id,
    string Group,
    string Subgroup,
    bool AboveRanges,
    string Record,
    ReferenceProject Project,
    ReferenceComponent Component,
    string Selection,
    ReferenceLabel? Label);

internal sealed record ReferenceProject(string Criticality, string Exposure);

internal sealed record ReferenceComponent(
    string? ProductKey,
    string? Purl,
    string Name,
    string Kind,
    string? Version,
    string Packaging,
    string? DependencyScope);

internal sealed record ReferenceLabel(string Verdict, int Level, string Reason, string By, bool Reviewed)
{
    public const string Affected = "affected";
    public const string NotAffected = "not_affected";
    public const string Undecidable = "undecidable";

    [JsonIgnore]
    public bool ByPerson => Reviewed || !By.Contains("model", StringComparison.OrdinalIgnoreCase);
}

internal sealed record ReferenceOutcome(ReferencePair Pair, int Level, string? MatchType, string? VersionVerdict, string? VersionReason);

internal sealed record ReferenceEvaluation(IReadOnlyList<ReferenceOutcome> Outcomes)
{
    public const double MaxMissedShare = 0.02;
    public const double MaxUnfoundedShare = 0.20;
    public const int MinPairs = 200;
    public const int MinAboveRanges = 30;

    public IReadOnlyList<ReferenceOutcome> Labelled => [.. Outcomes.Where(o => o.Pair.Label is not null)];

    public IReadOnlyList<ReferenceOutcome> LabelledAffected => [.. Labelled.Where(o => o.Pair.Label!.Verdict == ReferenceLabel.Affected)];

    public IReadOnlyList<ReferenceOutcome> LabelledNotAffected => [.. Labelled.Where(o => o.Pair.Label!.Verdict == ReferenceLabel.NotAffected)];

    public IReadOnlyList<ReferenceOutcome> Missed => [.. LabelledAffected.Where(o => o.Level <= 1)];

    public IReadOnlyList<ReferenceOutcome> Unfounded => [.. LabelledNotAffected.Where(o => o.Level >= 3)];

    public IReadOnlyList<ReferenceOutcome> MissedExact => [.. Missed.Where(o => o.Pair.Group == "exact")];

    public double MissedShare => LabelledAffected.Count == 0 ? 0 : (double)Missed.Count / LabelledAffected.Count;

    public double UnfoundedShare => LabelledNotAffected.Count == 0 ? 0 : (double)Unfounded.Count / LabelledNotAffected.Count;

    public double UnknownShare => Outcomes.Count == 0 ? 0 : (double)Outcomes.Count(o => o.VersionVerdict == "unknown") / Outcomes.Count;

    public IReadOnlyList<string> Failures()
    {
        var failures = new List<string>();
        if (Labelled.Count < MinPairs)
        {
            failures.Add($"{Labelled.Count} labelled pairs, at least {MinPairs} needed.");
        }

        var above = Labelled.Where(o => o.Pair.AboveRanges).ToList();
        var aboveAffected = above.Count(o => o.Pair.Label!.Verdict == ReferenceLabel.Affected);
        var aboveNot = above.Count(o => o.Pair.Label!.Verdict == ReferenceLabel.NotAffected);
        if (aboveAffected < MinAboveRanges / 2 || aboveNot < MinAboveRanges / 2)
        {
            failures.Add($"Above every range: {aboveAffected} affected and {aboveNot} not affected, at least {MinAboveRanges / 2} of each needed.");
        }

        if (MissedExact.Count > 0)
        {
            failures.Add($"{MissedExact.Count} missed among the exact hits: {string.Join(", ", MissedExact.Select(o => o.Pair.Id))}.");
        }

        if (MissedShare > MaxMissedShare)
        {
            failures.Add($"{MissedShare:P1} missed ({Missed.Count} of {LabelledAffected.Count}), at most {MaxMissedShare:P0}.");
        }

        if (UnfoundedShare > MaxUnfoundedShare)
        {
            failures.Add($"{UnfoundedShare:P1} unfounded ({Unfounded.Count} of {LabelledNotAffected.Count}), at most {MaxUnfoundedShare:P0}.");
        }

        return failures;
    }

    public string Report(TimeSpan duration)
    {
        var text = new StringBuilder();
        text.AppendLine("# Reference set evaluation").AppendLine();
        text.AppendLine($"Pairs {Outcomes.Count}, labelled {Labelled.Count} (by a person or reviewed: {Labelled.Count(o => o.Pair.Label!.ByPerson)}), above every range {Labelled.Count(o => o.Pair.AboveRanges)} ({Labelled.Count(o => o.Pair.AboveRanges && o.Pair.Label!.Verdict == ReferenceLabel.Affected)} affected). Run {duration.TotalSeconds:F0} s.").AppendLine();
        text.AppendLine("| Measure | Value | Threshold |").AppendLine("|---|---:|---:|");
        text.AppendLine($"| missed among exact hits | {MissedExact.Count} | 0 |");
        text.AppendLine($"| missed | {Missed.Count} of {LabelledAffected.Count} ({MissedShare:P1}) | {MaxMissedShare:P0} |");
        text.AppendLine($"| unfounded | {Unfounded.Count} of {LabelledNotAffected.Count} ({UnfoundedShare:P1}) | {MaxUnfoundedShare:P0} |");
        text.AppendLine($"| version check unknown | {UnknownShare:P1} | (measured) |");
        var agree = Labelled.Count(o => o.Level == o.Pair.Label!.Level);
        var near = Labelled.Count(o => Math.Abs(o.Level - o.Pair.Label!.Level) <= 1);
        text.AppendLine($"| level as labelled | {agree} of {Labelled.Count} | (calibration) |");
        text.AppendLine($"| level within one | {near} of {Labelled.Count} | (calibration) |").AppendLine();

        text.AppendLine("## Levels: labelled (rows) against computed (columns)").AppendLine();
        text.AppendLine("| | 1 | 2 | 3 | 4 | 5 |").AppendLine("|---|---:|---:|---:|---:|---:|");
        for (var expected = 1; expected <= 5; expected++)
        {
            var row = Labelled.Where(o => o.Pair.Label!.Level == expected).ToList();
            text.AppendLine($"| {expected} | {string.Join(" | ", Enumerable.Range(1, 5).Select(l => row.Count(o => o.Level == l)))} |");
        }

        text.AppendLine().AppendLine("## Computed levels and version checks by group, labelled or not").AppendLine();
        text.AppendLine("| Group | Pairs | no finding | 1 | 2 | 3 | 4 | 5 | in range / out / unknown |").AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var group in Outcomes.GroupBy(o => $"{o.Pair.Group} / {o.Pair.Subgroup}").OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var levels = string.Join(" | ", Enumerable.Range(1, 5).Select(l => group.Count(o => o.MatchType is not null && o.Level == l)));
            var checks = $"{group.Count(o => o.VersionVerdict == "in_range")} / {group.Count(o => o.VersionVerdict == "out_of_range")} / {group.Count(o => o.VersionVerdict == "unknown")}";
            text.AppendLine($"| {group.Key} | {group.Count()} | {group.Count(o => o.MatchType is null)} | {levels} | {checks} |");
        }

        text.AppendLine().AppendLine("## By group").AppendLine();
        text.AppendLine("| Group | Pairs | affected / not / undecidable | missed | unfounded | level as labelled |").AppendLine("|---|---:|---|---:|---:|---:|");
        foreach (var group in Labelled.GroupBy(o => $"{o.Pair.Group} / {o.Pair.Subgroup}").OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var verdicts = $"{group.Count(o => o.Pair.Label!.Verdict == ReferenceLabel.Affected)} / {group.Count(o => o.Pair.Label!.Verdict == ReferenceLabel.NotAffected)} / {group.Count(o => o.Pair.Label!.Verdict == ReferenceLabel.Undecidable)}";
            text.AppendLine($"| {group.Key} | {group.Count()} | {verdicts} | {group.Count(Missed.Contains)} | {group.Count(Unfounded.Contains)} | {group.Count(o => o.Level == o.Pair.Label!.Level)} |");
        }

        text.AppendLine().AppendLine("## Disagreements").AppendLine();
        text.AppendLine("| Pair | Record | Component | Label | Computed | Version check | Reason |").AppendLine("|---|---|---|---|---|---|---|");
        foreach (var o in Labelled.Where(o => o.Level != o.Pair.Label!.Level).OrderBy(o => o.Pair.Id, StringComparer.Ordinal))
        {
            var mark = Missed.Contains(o) ? " **missed**" : Unfounded.Contains(o) ? " **unfounded**" : string.Empty;
            var component = $"{o.Pair.Component.ProductKey ?? o.Pair.Component.Purl} {o.Pair.Component.Version ?? "(no version)"}";
            text.AppendLine($"| {o.Pair.Id}{mark} | {o.Pair.Record} | {component} | {o.Pair.Label!.Verdict} {o.Pair.Label.Level} | {o.Level} {o.MatchType ?? "no finding"} | {o.VersionVerdict} {o.VersionReason} | {o.Pair.Label.Reason.Replace('|', '/')} |");
        }

        return text.ToString();
    }
}
