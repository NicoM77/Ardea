namespace Ardea.Intel.Contracts;

/// <summary>
/// Read access to consolidated entries for the Assessment module (master plan 3.2, 7.2): global data,
/// readable in every unit of work, with or without an organisation.
/// </summary>
public interface IEntryQueries
{
    /// <summary>The entry with its affected products, or <c>null</c> when it does not exist.</summary>
    Task<EntryFacts?> FindAsync(Guid entryId, CancellationToken cancellationToken);

    /// <summary>The entries with their affected products; unknown ids are left out.</summary>
    Task<IReadOnlyList<EntryFacts>> FindManyAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken);

    /// <summary>
    /// The reverse search (master plan 7.2): vulnerabilities, published or disputed and changed since
    /// <paramref name="since"/>, that name one of the keys as an affected product.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindAffectingAsync(AffectedKeys keys, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>The texts of the entries for the answers from templates (master plan 7.5); unknown ids are left out.</summary>
    Task<IReadOnlyList<EntryText>> TextsAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken);
}

/// <summary>
/// What a source says about an entry in its own words (master plan 7.5): title, description, solutions and
/// workarounds as the source wrote them, the source the description comes from, and the advisory to read.
/// </summary>
/// <param name="Id">The entry.</param>
/// <param name="Key">Its primary key, e.g. <c>CVE-2026-116325</c>.</param>
/// <param name="Title">Its title.</param>
/// <param name="Description">Its description in the source's words.</param>
/// <param name="DescriptionSource">The name of the source whose description the entry shows, e.g. <c>CVE Program</c>.</param>
/// <param name="Solutions">The sources' solutions, as written.</param>
/// <param name="Workarounds">The sources' workarounds, as written.</param>
/// <param name="AdvisoryUrl">A reference tagged as the vendor's advisory, else one tagged as a patch, else the first.</param>
public sealed record EntryText(
    Guid Id,
    string Key,
    string? Title,
    string? Description,
    string? DescriptionSource,
    IReadOnlyList<string> Solutions,
    IReadOnlyList<string> Workarounds,
    string? AdvisoryUrl);

/// <summary>Keys of affected products, in the forms of <c>intel.entry_affected</c>.</summary>
/// <param name="ProductKeys">Product keys: catalogue keys or raw keys.</param>
/// <param name="CpePairs">CPE vendor and product, as the catalogue writes them.</param>
/// <param name="Packages">purl type and name, normalised as the catalogue does.</param>
public sealed record AffectedKeys(
    IReadOnlyCollection<string> ProductKeys,
    IReadOnlyCollection<KeyPair> CpePairs,
    IReadOnlyCollection<KeyPair> Packages)
{
    public bool IsEmpty => ProductKeys.Count == 0 && CpePairs.Count == 0 && Packages.Count == 0;
}

/// <summary>Two parts of a key: CPE vendor and product, or purl type and name.</summary>
public sealed record KeyPair(string First, string Second);

/// <summary>What the assessment needs to know about an entry (master plan 4.4, 7.6).</summary>
public sealed record EntryFacts
{
    public required Guid Id { get; init; }

    /// <summary>The primary key, e.g. <c>CVE-2026-115731</c>.</summary>
    public required string Key { get; init; }

    /// <summary><c>vulnerability</c> or <c>warning</c>.</summary>
    public required string Kind { get; init; }

    /// <summary><c>published</c>, <c>rejected</c>, <c>disputed</c> or <c>merged</c>.</summary>
    public required string State { get; init; }

    public Guid? MergedIntoId { get; init; }

    public required int Revision { get; init; }

    public string? Title { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public required DateTimeOffset ChangedAt { get; init; }

    public double? CvssScore { get; init; }

    public string? CvssVersion { get; init; }

    public string? CvssVector { get; init; }

    /// <summary><c>none</c>, <c>poc</c> or <c>active</c>.</summary>
    public required string Exploitation { get; init; }

    /// <summary>The source that backs the exploitation state, e.g. <c>cisa-kev</c>; <c>null</c> for none.</summary>
    public string? ExploitationSource { get; init; }

    /// <summary>Since when that source reports it, if it says.</summary>
    public DateTimeOffset? ExploitationSince { get; init; }

    /// <summary><c>high</c> (EPSS above 50 %), <c>low</c>, or <c>null</c> without a value.</summary>
    public string? EpssClass { get; init; }

    public required bool FixAvailable { get; init; }

    /// <summary>A national authority warns about this entry (master plan 5.6, 7.6).</summary>
    public required bool AuthorityWarning { get; init; }

    public required IReadOnlyList<AffectedFacts> Affected { get; init; }
}

/// <summary>One version statement about an affected product from one source (<c>intel.entry_affected</c>).</summary>
public sealed record AffectedFacts
{
    public required Guid SourceId { get; init; }

    public string? Vendor { get; init; }

    public string? Product { get; init; }

    public string? ProductKey { get; init; }

    public string? CpeVendor { get; init; }

    public string? CpeProduct { get; init; }

    public string? PurlType { get; init; }

    public string? PurlName { get; init; }

    public string? VersionScheme { get; init; }

    public string? Introduced { get; init; }

    public string? Fixed { get; init; }

    public string? LastAffected { get; init; }

    public string? Exact { get; init; }

    /// <summary><c>affected</c>, <c>unaffected</c> or <c>unknown</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The record's status for versions it does not name; <c>unknown</c> when it says nothing.</summary>
    public required string DefaultStatus { get; init; }
}
