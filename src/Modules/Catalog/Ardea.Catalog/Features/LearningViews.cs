using System.ComponentModel;
using Ardea.Catalog.Contracts;
using Ardea.Catalog.Learning;
using Ardea.Catalog.Persistence;

namespace Ardea.Catalog.Features;

/// <summary>A new product as learned or as the administrator corrects it: the fields of a curated entry.</summary>
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

/// <param name="Decision"><c>alias</c>, <c>new</c> or <c>unclear</c>.</param>
/// <param name="AliasOf">For <c>alias</c>: the key of the entry.</param>
/// <param name="AliasName">For <c>alias</c>: that entry's name, when it is in effect.</param>
/// <param name="NewProduct">For <c>new</c>: the product.</param>
/// <param name="Confidence"><c>high</c>, <c>medium</c> or <c>low</c>.</param>
/// <param name="Reasoning">One sentence: the rule's, or the model's, to be shown as plain text.</param>
/// <param name="LearnedBy"><c>rule</c>, <c>model</c> or <c>admin</c>.</param>
/// <param name="Provider">The provider the model ran on.</param>
/// <param name="Model">The model.</param>
/// <param name="PromptVersion">The prompt's version.</param>
/// <param name="Hold">Why it waits for a person: <c>confidence</c>, <c>suspicious_input</c>, <c>invalid_output</c>, <c>unclear</c>, <c>check</c>.</param>
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

/// <summary>
/// One raw key the sources name and what became of it (master plan 6.9, the review list). <c>Outcome</c> is
/// <c>open</c>, <c>pending</c>, <c>alias</c>, <c>new</c>, <c>unclear</c>, <c>rejected</c> or <c>withdrawn</c>;
/// <c>ProductKey</c> for <c>alias</c> and <c>new</c> the key the raw key resolves to.
/// </summary>
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

/// <summary>
/// Confirms a proposal, or maps a raw key by hand. Without fields the stored proposal takes effect as it is; with
/// <see cref="AliasOf"/> the raw key becomes another spelling of that entry; with <see cref="NewProduct"/> a new entry.
/// </summary>
internal sealed record AdoptLearnedRequest(
    [property: Description("Key of the entry the raw key is another spelling of.")] string? AliasOf,
    LearnedProductInput? NewProduct);

/// <summary>A new product as the administrator writes it; the key follows from the name.</summary>
internal sealed record LearnedProductInput(
    string? Name,
    string? Vendor,
    [property: Description("os, runtime, framework, cms, database, messaging, container, network, virtualization, library, plugin, service or other")] string? Kind,
    string? VersionScheme,
    IReadOnlyList<string>? Cpe,
    IReadOnlyList<string>? Related,
    string? Platform);

/// <summary>The provider the catalogue asks (ADR 040).</summary>
/// <param name="Setting">The setting's value: a provider name, <c>none</c>, or <c>null</c> for the first local provider.</param>
/// <param name="Locked">Set by <c>ARDEA_AI__CATALOG_PROVIDER</c>: shown read-only.</param>
/// <param name="Name">The provider in effect, or <c>null</c> when the catalogue asks no model.</param>
/// <param name="Local">Whether it counts as local.</param>
/// <param name="Model">Its model.</param>
internal sealed record LearningProviderView(string? Setting, bool Locked, string? Name, bool Local, string? Model);

/// <summary>One week of learning (master plan 6.9 «Messgrössen»), Monday 00:00 UTC to the next.</summary>
/// <param name="Week">The Monday the week starts on.</param>
/// <param name="Learned">Proposals made, by rule or model.</param>
/// <param name="Adopted">Learned entries that took effect without a person.</param>
/// <param name="Confirmed">Proposals a person confirmed, changed or mapped by hand.</param>
/// <param name="Rejected">Proposals a person rejected.</param>
/// <param name="Withdrawn">Learned entries taken back.</param>
internal sealed record LearningWeekView(DateOnly Week, int Learned, int Adopted, int Confirmed, int Rejected, int Withdrawn);

/// <summary>
/// The measures of the catalogue that learns for the administration (master plan 6.8, 6.9): the share of unknown product
/// keys at the last collection, the raw keys by outcome, the model calls of the day against the quota, eight weeks.
/// </summary>
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

/// <summary>Absent leaves the setting as it is; an empty string removes it (first local provider); <c>none</c> switches the model off.</summary>
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
