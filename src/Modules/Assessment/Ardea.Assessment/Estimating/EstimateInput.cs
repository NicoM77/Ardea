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

internal sealed record EstimateInput
{
    public const int DescriptionLength = 4000;

    public const int RemedyLength = 500;
    public const int MaxRemedies = 5;
    public const int MaxAffected = 20;
    public const int MaxReferences = 10;

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

    [JsonIgnore]
    public IReadOnlyDictionary<string, Guid> ComponentIds { get; init; } = new Dictionary<string, Guid>();

    [JsonIgnore]
    public IReadOnlyDictionary<string, string> ComponentContexts { get; init; } = new Dictionary<string, string>();

    [JsonIgnore]
    public string Fingerprint { get; init; } = string.Empty;

    public string ToDataBlock() => "<source_data>\n" + JsonSerializer.Serialize(this, Json) + "\n</source_data>";

    public static IReadOnlyList<Finding> HitFindings(IEnumerable<Finding> findings) =>
        [.. findings.Where(f => f.Status == "open" && f.MatchType is "package" or "cpe" or "product")
            .OrderByDescending(f => f.Level).ThenBy(f => f.ComponentId)];

    public static EstimateInput Build(
        EntryFacts entry, EntryText? text, IReadOnlyList<Finding> hits, IReadOnlyDictionary<Guid, ProfileComponent> components,
        string sourceName, string language)
    {
        var projects = new Dictionary<Guid, string>();
        foreach (var finding in hits)
        {
            projects.TryAdd(finding.ProjectId, "P" + (projects.Count + 1).ToString(CultureInfo.InvariantCulture));
        }

        var refs = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var contexts = new Dictionary<string, string>(StringComparer.Ordinal);
        var inputComponents = new List<InputComponent>();
        foreach (var finding in hits)
        {
            var reference = "C" + (inputComponents.Count + 1).ToString(CultureInfo.InvariantCulture);
            refs[reference] = finding.ComponentId;
            var component = components.GetValueOrDefault(finding.ComponentId);
            contexts[reference] = Context(component, finding.VersionVerdict);
            inputComponents.Add(new InputComponent(
                reference,
                projects[finding.ProjectId],
                SharedKeys(finding, component),
                component?.Kind,
                component?.Version,
                finding.VersionVerdict ?? "unknown",
                finding.VersionReason,
                component?.Packaging,
                component?.Exposure ?? "internal",
                component?.Criticality ?? "normal",
                component?.Protection == "effective" ? "effective" : "none",
                component?.DependencyScope == "development" ? "development" : null));
        }

        var fingerprint = new StringBuilder().Append(CultureInfo.InvariantCulture, $"{entry.Id}|{entry.Revision}|{EstimatePrompt.Version}");
        foreach (var finding in hits.OrderBy(f => f.ComponentId))
        {
            fingerprint.Append(CultureInfo.InvariantCulture, $"|{finding.ComponentId}:{Context(components.GetValueOrDefault(finding.ComponentId), finding.VersionVerdict)}");
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
            ComponentContexts = contexts,
            Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString()))),
        };
    }

    public static string Context(ProfileComponent? component, string? versionVerdict) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            component?.Version, versionVerdict, component?.Packaging, component?.Exposure, component?.Criticality,
            component?.Protection == "effective" ? "effective" : "none", component?.DependencyScope == "development" ? "development" : null))))[..16];

    private static IReadOnlyList<string> SharedKeys(Finding finding, ProfileComponent? component) =>
        finding.Evidence.RootElement.ValueKind == JsonValueKind.Object
        && finding.Evidence.RootElement.TryGetProperty("match", out var match) && match.ValueKind == JsonValueKind.Object
        && match.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array
            ? [.. keys.EnumerateArray().Where(k => k.ValueKind == JsonValueKind.String).Select(k => k.GetString()!).Distinct(StringComparer.Ordinal)]
            : component?.ProductKey is { } key ? ["product " + key] : [];

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
    IReadOnlyList<string> Keys,
    string? Kind,
    string? Version,
    string VersionCheck,
    string? VersionReason,
    string? Packaging,
    string Exposure,
    string Criticality,
    string Protection,
    string? Scope);

internal sealed record InputSource(int N, string? Name, string? Url, IReadOnlyList<string>? Tags);
