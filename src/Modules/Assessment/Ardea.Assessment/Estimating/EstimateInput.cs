using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Intel.Contracts;

namespace Ardea.Assessment.Estimating;

/// <summary>
/// What the model sees of one alert (master plan 7.5 «Eingabe, bewusst knapp»; task 4.4): of the entry its title,
/// description, affected products with ranges, severity with vector, exploitation, remedies and a numbered source
/// list; of the profile only the hit components with version, exposure, criticality and protection. Components are
/// <c>C1</c>, <c>C2</c>, …; projects are <c>P1</c>, <c>P2</c>, … and their names are replaced wherever they appear.
/// Nothing else of the profile leaves the system.
/// </summary>
internal sealed record EstimateInput
{
    /// <summary>The description is cut here: a record's median size is 3.6 kB (master plan 7.10).</summary>
    public const int DescriptionLength = 4000;

    public const int RemedyLength = 500;
    public const int MaxRemedies = 5;
    public const int MaxAffected = 20;
    public const int MaxReferences = 10;

    /// <summary>The block's JSON escapes <c>&lt;</c> and <c>&gt;</c>: no source text can close the data block.</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public required InputEntry Entry { get; init; }

    public required IReadOnlyList<InputComponent> Components { get; init; }

    public required IReadOnlyList<InputSource> Sources { get; init; }

    public required string Language { get; init; }

    /// <summary>Placeholder → component id; never sent.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, Guid> ComponentIds { get; init; } = new Dictionary<string, Guid>();

    /// <summary>
    /// The input's hash (master plan 4.9 «Profilstand einer Einschätzung»): entry and revision, the hit components with
    /// version and context, rule set and prompt version. The same fingerprint asks the model nothing new.
    /// </summary>
    [JsonIgnore]
    public string Fingerprint { get; init; } = string.Empty;

    /// <summary>The data block of the user message.</summary>
    public string ToDataBlock() => "<source_data>\n" + JsonSerializer.Serialize(this, Json) + "\n</source_data>";

    /// <summary>The hit components of the alert: its open findings with a strong match, highest level first.</summary>
    public static IReadOnlyList<Finding> HitFindings(IEnumerable<Finding> findings) =>
        [.. findings.Where(f => f.Status is "open" or "in_review" && f.MatchType is "package" or "cpe" or "product")
            .OrderByDescending(f => f.Level).ThenBy(f => f.ComponentId)];

    public static EstimateInput Build(
        EntryFacts entry, EntryText? text, IReadOnlyList<Finding> hits, IReadOnlyDictionary<Guid, ProfileComponent> components,
        string sourceName, string language, int rulesetVersion)
    {
        var projects = new Dictionary<Guid, string>();
        var names = new List<(string Name, string Placeholder)>();
        foreach (var finding in hits)
        {
            if (!projects.ContainsKey(finding.ProjectId))
            {
                var placeholder = "P" + (projects.Count + 1).ToString(CultureInfo.InvariantCulture);
                projects[finding.ProjectId] = placeholder;
                if (components.GetValueOrDefault(finding.ComponentId)?.ProjectName is { Length: > 0 } name)
                {
                    names.Add((name, placeholder));
                }
            }
        }

        // Longest names first, so «Shop» does not eat into «Shop Backend».
        names.Sort((a, b) => b.Name.Length.CompareTo(a.Name.Length));
        string? Scrub(string? value)
        {
            if (value is null)
            {
                return null;
            }

            foreach (var (name, placeholder) in names)
            {
                value = value.Replace(name, placeholder, StringComparison.OrdinalIgnoreCase);
            }

            return value;
        }

        var refs = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var inputComponents = new List<InputComponent>();
        var fingerprint = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"{entry.Id}|{entry.Revision}|{rulesetVersion}|{EstimatePrompt.Version}");
        foreach (var finding in hits)
        {
            var reference = "C" + (inputComponents.Count + 1).ToString(CultureInfo.InvariantCulture);
            refs[reference] = finding.ComponentId;
            var component = components.GetValueOrDefault(finding.ComponentId);
            inputComponents.Add(new InputComponent(
                reference,
                projects[finding.ProjectId],
                Scrub(component?.Name) ?? "unknown",
                component?.ProductKey,
                Scrub(component?.Purl),
                component?.Kind,
                component?.Version,
                finding.VersionVerdict ?? "unknown",
                finding.VersionReason,
                component?.Packaging,
                component?.Exposure ?? "internal",
                component?.Criticality ?? "normal",
                component?.Protection ?? "none",
                Cut(Scrub(component?.ProtectionNote), RemedyLength),
                component?.DependencyScope == "development" ? "development" : null));
        }

        foreach (var finding in hits.OrderBy(f => f.ComponentId))
        {
            var c = components.GetValueOrDefault(finding.ComponentId);
            fingerprint.Append(CultureInfo.InvariantCulture,
                $"|{finding.ComponentId}:{c?.Version}:{finding.VersionVerdict}:{c?.Exposure}:{c?.Criticality}:{c?.Protection}:{c?.ProtectionNote}:{c?.DependencyScope}");
        }

        var sources = new List<InputSource> { new(1, sourceName, null, ["record"]) };
        foreach (var reference in (text?.References ?? []).Take(MaxReferences))
        {
            sources.Add(new InputSource(sources.Count + 1, null, reference.Url, reference.Tags.Count == 0 ? null : reference.Tags));
        }

        var affected = entry.Affected
            .Select(a => new InputAffected(
                a.Vendor, a.Product ?? a.ProductKey ?? a.CpeProduct ?? a.PurlName,
                a.PurlType is null ? null : $"pkg:{a.PurlType}/{a.PurlName}", a.Introduced, a.Fixed, a.LastAffected, a.Exact, a.Status))
            .Distinct().Take(MaxAffected).ToList();
        return new EstimateInput
        {
            Entry = new InputEntry(
                entry.Key,
                entry.Title,
                Cut(text?.Description, DescriptionLength),
                new InputSeverity(entry.CvssScore, entry.CvssVersion, entry.CvssVector),
                new InputExploitation(entry.Exploitation, entry.ExploitationSource, entry.ExploitationSince?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                entry.EpssClass,
                entry.AuthorityWarning,
                affected,
                [.. (text?.Solutions ?? []).Take(MaxRemedies).Select(s => Cut(s, RemedyLength)!)],
                [.. (text?.Workarounds ?? []).Take(MaxRemedies).Select(s => Cut(s, RemedyLength)!)]),
            Components = inputComponents,
            Sources = sources,
            Language = language,
            ComponentIds = refs,
            Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString()))),
        };
    }

    private static string? Cut(string? value, int length) => value is null || value.Length <= length ? value : value[..length] + "…";
}

internal sealed record InputEntry(
    string Key,
    string? Title,
    string? Description,
    InputSeverity Severity,
    InputExploitation Exploitation,
    string? Epss,
    bool AuthorityWarning,
    IReadOnlyList<InputAffected> Affected,
    IReadOnlyList<string> Solutions,
    IReadOnlyList<string> Workarounds);

internal sealed record InputSeverity(double? CvssScore, string? CvssVersion, string? Vector);

internal sealed record InputExploitation(string State, string? Source, string? Since);

internal sealed record InputAffected(string? Vendor, string? Product, string? Package, string? Introduced, string? Fixed, string? LastAffected, string? Exact, string Status);

internal sealed record InputComponent(
    string Ref,
    string Project,
    string Name,
    string? Product,
    string? Package,
    string? Kind,
    string? Version,
    string VersionCheck,
    string? VersionReason,
    string? Packaging,
    string Exposure,
    string Criticality,
    string Protection,
    string? ProtectionNote,
    string? Scope);

/// <param name="N">The number the model refers to.</param>
/// <param name="Name">The source of the record, for number 1.</param>
/// <param name="Url">The reference's address.</param>
/// <param name="Tags">What kind of reference it is, e.g. <c>vendor-advisory</c>.</param>
internal sealed record InputSource(int N, string? Name, string? Url, IReadOnlyList<string>? Tags);
