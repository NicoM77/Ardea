namespace Ardea.Intel.Sources.Abstractions;

public interface ISourceProvider
{
    SourceDescriptor Descriptor { get; }

    IAsyncEnumerable<FetchItem> FetchAsync(SyncContext context, CancellationToken cancellationToken);

    NormalizationResult Normalize(RawRecord record);
}

public sealed record Signal(string Scheme, string Value)
{
    public DateOnly? KevListedOn { get; init; }

    public Uri? KevUrl { get; init; }

    public bool? KevKnownRansomware { get; init; }

    public double? EpssScore { get; init; }

    public double? EpssPercentile { get; init; }
}

public interface ISignalProvider
{
    SourceDescriptor Descriptor { get; }

    IAsyncEnumerable<Signal> FetchSignalsAsync(SyncContext context, CancellationToken cancellationToken);
}
