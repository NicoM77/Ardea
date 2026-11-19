namespace Ardea.Catalog.Contracts;

/// <summary>
/// A product key the sources name that the catalogue does not know (master plan 6.5, 6.9), with what the records say
/// about it. Public data of the sources only; nothing of an organisation.
/// </summary>
/// <param name="RawKey">The raw key, e.g. <c>acme/widget</c>.</param>
/// <param name="Vendor">The vendor as a record writes it.</param>
/// <param name="Product">The product as a record writes it.</param>
/// <param name="CpePairs">The CPE pairs the records give for it, as <c>vendor:product</c>.</param>
/// <param name="Purl">A package URL without version the records give for it.</param>
/// <param name="RecordCount">The entries that name it.</param>
/// <param name="InKev">One of them is listed in CISA KEV.</param>
public sealed record UnknownProductStatement(
    string RawKey, string? Vendor, string Product, IReadOnlyList<string> CpePairs, string? Purl, int RecordCount, bool InKev);

/// <summary>What the sources name and how much of it the catalogue does not know (master plan 6.8 «Anteil unbekannter Produkte»).</summary>
/// <param name="ProductKeys">Distinct product keys of affected products in entries.</param>
/// <param name="Unknown">The product keys among them the catalogue does not know, with what the records say.</param>
public sealed record UnknownProductCensus(int ProductKeys, IReadOnlyList<UnknownProductStatement> Unknown);

/// <summary>
/// The unknown products of the sources (master plan 6.9 step 1). Intel implements it; the catalogue learns from it and
/// never reads the entries itself.
/// </summary>
public interface IUnknownProductFeed
{
    /// <summary>Every product key of the entries the catalogue does not know, with its records.</summary>
    Task<UnknownProductCensus> CollectAsync(CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="limit"/> titles of entries that name the raw key, newest first.</summary>
    Task<IReadOnlyList<string>> TitlesAsync(string rawKey, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Learned entries took effect or were withdrawn (master plan 6.9 steps 5 and 6). The entries whose affected products
/// may resolve differently now are computed again: those that name one of <paramref name="RawKeys"/> (as their key or
/// as the raw key a learned entry resolved), carry one of <paramref name="ProductKeys"/>, or name one of the CPE pairs
/// or packages.
/// </summary>
/// <param name="RawKeys">The raw keys whose resolution changed.</param>
/// <param name="ProductKeys">Keys of learned entries that were withdrawn.</param>
/// <param name="CpePairs">CPE pairs of learned entries, as <c>vendor:product</c>.</param>
/// <param name="Packages">Package URLs without version of learned entries.</param>
/// <param name="Delay">How long to wait before the recomputation, so that every process reads the new catalogue.</param>
public sealed record LearnedEntriesChanged(
    IReadOnlyList<string> RawKeys, IReadOnlyList<string> ProductKeys, IReadOnlyList<string> CpePairs, IReadOnlyList<string> Packages, TimeSpan Delay);

/// <summary>Hears about learned entries; Intel recomputes the entries concerned (master plan 6.9 step 6).</summary>
public interface ICatalogChangeSubscriber
{
    Task OnLearnedEntriesChangedAsync(LearnedEntriesChanged change, CancellationToken cancellationToken);
}
