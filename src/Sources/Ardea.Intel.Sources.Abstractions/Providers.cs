namespace Ardea.Intel.Sources.Abstractions;

/// <summary>
/// A source adapter (master plan 5.2): fetch raw records, translate them. Nothing else: identity,
/// merging and meaning belong to the core (chapter 6).
/// </summary>
public interface ISourceProvider
{
    SourceDescriptor Descriptor { get; }

    /// <summary>New or changed raw records, with checkpoints in between.</summary>
    IAsyncEnumerable<FetchItem> FetchAsync(SyncContext context, CancellationToken cancellationToken);

    /// <summary>Translates one raw record. Pure and deterministic: no I/O, no clock, no randomness.</summary>
    NormalizationResult Normalize(RawRecord record);
}

/// <summary>A signal per identifier (master plan 4.4): KEV listing or EPSS score.</summary>
public sealed record Signal(string Scheme, string Value)
{
    public DateOnly? KevListedOn { get; init; }

    public Uri? KevUrl { get; init; }

    public bool? KevKnownRansomware { get; init; }

    public double? EpssScore { get; init; }

    public double? EpssPercentile { get; init; }
}

/// <summary>Signal sources write per identifier into <c>entry_signal</c>, not as raw records (master plan 4.4, 5.2).</summary>
public interface ISignalProvider
{
    SourceDescriptor Descriptor { get; }

    /// <summary>The complete current set of the source's signals.</summary>
    IAsyncEnumerable<Signal> FetchSignalsAsync(SyncContext context, CancellationToken cancellationToken);
}
