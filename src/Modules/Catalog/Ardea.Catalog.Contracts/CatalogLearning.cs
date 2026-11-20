namespace Ardea.Catalog.Contracts;

public sealed record UnknownProductStatement(
    string RawKey, string? Vendor, string Product, IReadOnlyList<string> CpePairs, string? Purl, int RecordCount, bool InKev);

public sealed record UnknownProductCensus(int ProductKeys, IReadOnlyList<UnknownProductStatement> Unknown);

public interface IUnknownProductFeed
{
    Task<UnknownProductCensus> CollectAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> TitlesAsync(string rawKey, int limit, CancellationToken cancellationToken);
}

public sealed record LearnedEntriesChanged(
    IReadOnlyList<string> RawKeys, IReadOnlyList<string> ProductKeys, IReadOnlyList<string> CpePairs, IReadOnlyList<string> Packages);

public interface ICatalogChangeSubscriber
{
    Task OnLearnedEntriesChangedAsync(LearnedEntriesChanged change, CancellationToken cancellationToken);
}

public interface ICatalogGrowth
{
    Task RequestAsync(TimeSpan delay, CancellationToken cancellationToken);
}
