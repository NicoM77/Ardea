using System.ComponentModel;
using Ardea.Catalog.Contracts;
using Ardea.Catalog.Learning;
using Ardea.Catalog.Persistence;

namespace Ardea.Catalog.Features;

internal sealed record LearnedProductView(
    string Key,
    string Name,
    string? Vendor,
    string Kind,
    string VersionScheme,
    IReadOnlyList<string> Cpe,
    IReadOnlyList<string> Packages,
    IReadOnlyList<ProductAlias> Aliases,
    IReadOnlyList<string> Related,
    string? Platform);

internal sealed record LearnedProposalView(
    string Decision,
    string? AliasOf,
    string? AliasName,
    LearnedProductView? NewProduct,
    string Confidence,
    string? Reasoning,
    string LearnedBy,
    string? Provider,
    string? Model,
    string? PromptVersion,
    string? Hold);

internal sealed record LearnedItemView(
    Guid Id,
    string RawKey,
    string? Vendor,
    string Product,
    IReadOnlyList<string> CpePairs,
    string? Purl,
    int RecordCount,
    bool InKev,
    string Outcome,
    string? ProductKey,
    LearnedProposalView? Proposal,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? DecidedAt);

internal sealed record AdoptLearnedRequest(
    [property: Description("Key of the entry the raw key is another spelling of.")] string? AliasOf,
    LearnedProductInput? NewProduct);

internal sealed record LearnedProductInput(
    string? Name,
    string? Vendor,
    [property: Description("os, runtime, framework, cms, database, messaging, container, network, virtualization, library, plugin, service or other")] string? Kind,
    string? VersionScheme,
    IReadOnlyList<string>? Cpe,
    IReadOnlyList<string>? Related,
    string? Platform);

internal sealed record LearningProviderView(string? Setting, bool Locked, string? Name, bool Local, string? Model);

internal sealed record LearningWeekView(DateOnly Week, int Learned, int Adopted, int Confirmed, int Rejected, int Withdrawn);

internal sealed record LearningStatusView(
    LearningProviderView Provider,
    int CallsToday,
    int DailyCalls,
    int? ProductKeys,
    int? UnknownKeys,
    double? UnknownShare,
    DateTimeOffset? CollectedAt,
    IReadOnlyDictionary<string, int> Outcomes,
    IReadOnlyList<LearningWeekView> Weeks);

internal sealed record UpdateLearningRequest(string? Provider);

internal static class LearningViewMapping
{
    public static LearnedItemView View(UnknownProductRow row, Func<string, string?> nameOf)
    {
        var proposal = LearnedProposal.Read(row.Proposal);
        return new LearnedItemView(
            row.Id, row.RawKey, row.Vendor, row.Product, row.CpePairs, row.Purl, row.RecordCount, row.InKev, row.Outcome, row.ProductKey,
            proposal is null ? null : new LearnedProposalView(
                proposal.Decision, proposal.AliasOf, proposal.AliasOf is { } alias ? nameOf(alias) : null,
                proposal.NewProduct is { } p ? View(p) : null,
                proposal.Confidence, proposal.Reasoning, proposal.LearnedBy, proposal.Provider, proposal.Model, proposal.PromptVersion, proposal.Hold),
            row.FirstSeenAt, row.LastAttemptAt, row.DecidedAt);
    }

    public static LearnedProductView View(ProposedProduct p) =>
        new(p.Key, p.Name, p.Vendor, p.Kind, p.VersionScheme, p.Cpe, p.Packages, p.Aliases, p.Related, p.Platform);
}
