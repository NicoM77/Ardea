namespace Ardea.Intel.Contracts;

public interface IEntryQueries
{
    Task<EntryFacts?> FindAsync(Guid entryId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EntryFacts>> FindManyAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> FindAffectingAsync(AffectedKeys keys, DateTimeOffset since, CancellationToken cancellationToken);

    Task<IReadOnlyList<EntryText>> TextsAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken);
}

public sealed record EntryText(
    Guid Id,
    string Key,
    string? Title,
    string? Description,
    string? DescriptionSource,
    IReadOnlyList<string> Solutions,
    IReadOnlyList<string> Workarounds,
    string? AdvisoryUrl)
{
    public IReadOnlyList<EntryReference> References { get; init; } = [];
}

public sealed record EntryReference(string Url, IReadOnlyList<string> Tags);

public sealed record AffectedKeys(
    IReadOnlyCollection<string> ProductKeys,
    IReadOnlyCollection<KeyPair> CpePairs,
    IReadOnlyCollection<KeyPair> Packages)
{
    public bool IsEmpty => ProductKeys.Count == 0 && CpePairs.Count == 0 && Packages.Count == 0;
}

public sealed record KeyPair(string First, string Second);

public sealed record EntryFacts
{
    public required Guid Id { get; init; }

    public required string Key { get; init; }

    public required string Kind { get; init; }

    public required string State { get; init; }

    public Guid? MergedIntoId { get; init; }

    public required int Revision { get; init; }

    public string? Title { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public required DateTimeOffset ChangedAt { get; init; }

    public double? CvssScore { get; init; }

    public string? CvssVersion { get; init; }

    public string? CvssVector { get; init; }

    public required string Exploitation { get; init; }

    public string? ExploitationSource { get; init; }

    public DateTimeOffset? ExploitationSince { get; init; }

    public bool InKev { get; init; }

    public string? EpssClass { get; init; }

    public required bool FixAvailable { get; init; }

    public required bool AuthorityWarning { get; init; }

    public required IReadOnlyList<AffectedFacts> Affected { get; init; }

    public IReadOnlyList<string> FixVersionsInText { get; init; } = [];
}

public sealed record AffectedFacts
{
    public required Guid SourceId { get; init; }

    public string? Vendor { get; init; }

    public string? Product { get; init; }

    public string? ProductKey { get; init; }

    public string? LearnedFrom { get; init; }

    public string? CpeVendor { get; init; }

    public string? CpeProduct { get; init; }

    public string? PurlType { get; init; }

    public string? PurlName { get; init; }

    public string? VersionScheme { get; init; }

    public string? Introduced { get; init; }

    public string? Fixed { get; init; }

    public string? LastAffected { get; init; }

    public string? Exact { get; init; }

    public required string Status { get; init; }

    public required string DefaultStatus { get; init; }
}
