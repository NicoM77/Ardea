using System.Text.Json;
using System.Text.Json.Serialization;
using Ardea.Catalog.Contracts;

namespace Ardea.Catalog.Learning;

internal static class LearnedBy
{
    public const string Rule = "rule";
    public const string Model = "model";

    public const string Admin = "admin";
}

internal static class Decisions
{
    public const string Alias = "alias";

    public const string New = "new";

    public const string Unclear = "unclear";
}

internal static class Holds
{
    public const string Confidence = "confidence";

    public const string SuspiciousInput = "suspicious_input";

    public const string InvalidOutput = "invalid_output";

    public const string Unclear = "unclear";

    public const string Check = "check";
}

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
