namespace Ardea.Intel.Features;

internal sealed record EntrySummary(
    string Key,
    string Kind,
    string? Title,
    string State,
    double? CvssScore,
    string Severity,
    string Exploitation,
    string? EpssClass,
    bool FixAvailable,
    bool AuthorityWarning,
    DateTimeOffset? PublishedAt,
    DateTimeOffset ChangedAt,
    IReadOnlyList<string> Aliases);

/// <summary>A page of a list (master plan 10.1: cursor pagination).</summary>
internal sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

internal sealed record AffectedView(
    string? Vendor,
    string? Product,
    string? ProductKey,
    string? Cpe,
    string? Purl,
    string? VersionScheme,
    string? Introduced,
    string? Fixed,
    string? LastAffected,
    string? Exact,
    string Status,
    string DefaultStatus,
    string Source);

internal sealed record ReferenceView(string Url, IReadOnlyList<string> Tags);

internal sealed record MetricView(string System, string Version, string? Vector, double? Score, string? Assigner);

internal sealed record EvidenceView(string Source, string State, string? Evidence, DateTimeOffset? Since);

internal sealed record EntryDetail(
    EntrySummary Entry,
    string? Summary,
    string? Description,
    string? CvssVersion,
    string? CvssVector,
    IReadOnlyList<string> CweIds,
    IReadOnlyDictionary<string, string> Provenance,
    IReadOnlyList<EvidenceView> ExploitationEvidence,
    IReadOnlyList<MetricView> Metrics,
    IReadOnlyList<string> Solutions,
    IReadOnlyList<string> Workarounds,
    IReadOnlyList<AffectedView> Affected,
    IReadOnlyList<ReferenceView> References,
    int Revision,
    string? RedirectedFrom);

internal sealed record EntryChangeView(int Revision, DateTimeOffset ChangedAt, IReadOnlyList<string> Kinds, bool IsMaterial, string? TriggeredBy, System.Text.Json.JsonElement? Diff);

internal sealed record EntrySourceView(string Source, string Name, string Operator, string OperatorKind, string Relation, string? Url, string Attribution);
