using System.Text.Json;
using System.Text.Json.Serialization;
using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Learning;

/// <summary>Who learned something about a raw key (<c>learned_by</c>, master plan 4.5).</summary>
internal static class LearnedBy
{
    public const string Rule = "rule";
    public const string Model = "model";

    /// <summary>The instance administrator mapped it by hand or changed a proposal.</summary>
    public const string Admin = "admin";
}

/// <summary>What was learned about a raw key (master plan 6.9 step 3).</summary>
internal static class Decisions
{
    /// <summary>The raw key is another spelling of an entry: <see cref="LearnedProposal.AliasOf"/>.</summary>
    public const string Alias = "alias";

    /// <summary>The raw key is a product the catalogue lacks: <see cref="LearnedProposal.NewProduct"/>.</summary>
    public const string New = "new";

    public const string Unclear = "unclear";
}

/// <summary>Why a proposal waits for a person instead of taking effect (master plan 6.9 step 5).</summary>
internal static class Holds
{
    /// <summary>The model was not highly confident.</summary>
    public const string Confidence = "confidence";

    /// <summary>The source's product statement reads like an instruction; nothing of it takes effect without a person.</summary>
    public const string SuspiciousInput = "suspicious_input";

    /// <summary>The model's answers failed the check twice.</summary>
    public const string InvalidOutput = "invalid_output";

    /// <summary>The model could not say.</summary>
    public const string Unclear = "unclear";

    /// <summary>The answer passed its check, but the catalogue had changed by the time it was written.</summary>
    public const string Check = "check";
}

/// <summary>A product the catalogue lacks, as learned: the fields of a curated entry (ADR 030).</summary>
internal sealed record ProposedProduct(
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

/// <summary>
/// What was learned about one raw key and by whom (<c>unknown_product.proposal</c>). For the model also its provider, model
/// and prompt version; <see cref="Hold"/> says why it waits on the review list, when it does.
/// </summary>
internal sealed record LearnedProposal(
    string Decision,
    string? AliasOf,
    ProposedProduct? NewProduct,
    string Confidence,
    string? Reasoning,
    string LearnedBy,
    string? Provider = null,
    string? Model = null,
    string? PromptVersion = null,
    string? Hold = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static LearnedProposal? Read(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LearnedProposal>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
