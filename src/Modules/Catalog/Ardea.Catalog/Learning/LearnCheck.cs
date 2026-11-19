using System.Text.Json;
using Ardea.Catalog.Contracts;
using Ardea.Catalog.Products;

namespace Ardea.Catalog.Learning;

/// <summary>
/// Checks the model's answer before anything of it is stored (master plan 6.9 step 4): the schema without unknown or
/// missing fields and with the exact values of the fixed lists; every key from the candidate list, every CPE pair from
/// the input; a new product that is well formed and whose name no entry has (<see cref="ProductDrafts"/>). A failed
/// check names the rule, never the text.
/// </summary>
internal static class LearnCheck
{
    public static (LearnedProposal? Proposal, string? Problem) Check(string? text, LearnInput input, CatalogLayers catalog, ISet<string> keys)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, "empty");
        }

        var json = Unfence(text);
        LearnAnswer? answer;
        try
        {
            using (var document = JsonDocument.Parse(json))
            {
                if (!ExactValues(document.RootElement))
                {
                    return (null, "schema");
                }
            }

            answer = JsonSerializer.Deserialize<LearnAnswer>(json, LearnAnswer.Json);
        }
        catch (JsonException)
        {
            return (null, "schema");
        }

        if (answer is null || !Enum.IsDefined(answer.Decision) || !Enum.IsDefined(answer.Confidence))
        {
            return (null, "schema");
        }

        if (!ProductDrafts.Text(answer.Reasoning, ProductDrafts.ReasoningLength))
        {
            return (null, "reasoning");
        }

        var candidates = input.Candidates.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var confidence = Name(answer.Confidence);
        var reasoning = answer.Reasoning.Trim();
        switch (answer.Decision)
        {
            case LearnDecision.Alias:
                if (answer.NewProduct is not null || answer.AliasOf is null)
                {
                    return (null, "decision_fields");
                }

                if (!candidates.Contains(answer.AliasOf) || catalog.FindByKey(answer.AliasOf) is null)
                {
                    return (null, "unknown_key");
                }

                return (new LearnedProposal(Decisions.Alias, answer.AliasOf, null, confidence, reasoning, LearnedBy.Model), null);

            case LearnDecision.New:
                if (answer.AliasOf is not null || answer.NewProduct is not { } p || p.Cpe is null || p.Related is null)
                {
                    return (null, "decision_fields");
                }

                var name = p.Name?.Trim() ?? string.Empty;
                var vendor = string.IsNullOrWhiteSpace(p.Vendor) ? null : p.Vendor.Trim();
                if (!ProductDrafts.Text(name, ProductDrafts.NameLength))
                {
                    return (null, "name");
                }

                var key = ProductDrafts.KeyFor(name, vendor, k => keys.Contains(k) || catalog.FindByKey(k) is not null);
                if (key is null)
                {
                    return (null, "key");
                }

                var statement = new UnknownProductStatement(input.RawKey, input.Unknown.Vendor, input.Unknown.Product, input.Unknown.Cpe, input.Unknown.Purl, input.Unknown.Records, false);
                var product = new ProposedProduct(
                    key, name, vendor, p.Kind, p.VersionScheme, [.. p.Cpe.Distinct(StringComparer.Ordinal)],
                    PackageCoordinate.TryParsePurl(input.Unknown.Purl, out var package, out _) ? [package.ToString()] : [],
                    RuleMapping.RecordSpelling(statement, name, vendor), [.. p.Related.Distinct(StringComparer.Ordinal)], p.Platform);
                var allowedCpe = input.Unknown.Cpe.Select(ProductDrafts.CpeText).OfType<string>().ToHashSet(StringComparer.Ordinal);
                if (ProductDrafts.Problem(product, catalog, keys, allowedCpe, candidates) is { } problem)
                {
                    return (null, problem);
                }

                return (new LearnedProposal(Decisions.New, null, product, confidence, reasoning, LearnedBy.Model), null);

            default:
                if (answer.AliasOf is not null || answer.NewProduct is not null)
                {
                    return (null, "decision_fields");
                }

                return (new LearnedProposal(Decisions.Unclear, null, null, confidence, reasoning, LearnedBy.Model, Hold: Holds.Unclear), null);
        }
    }

    public static string Name<T>(T value)
        where T : struct, Enum => JsonSerializer.Serialize(value, LearnAnswer.Json).Trim('"');

    /// <summary>The fixed lists take exactly their snake_case names; the enum converter alone would also take <c>Alias</c> or <c>alias, new</c>.</summary>
    private static bool ExactValues(JsonElement root)
    {
        static bool Exact<T>(JsonElement parent, string property)
            where T : struct, Enum =>
            parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value)
            || (value.ValueKind == JsonValueKind.String && Enum.GetValues<T>().Select(Name).Contains(value.GetString(), StringComparer.Ordinal));

        return Exact<LearnDecision>(root, "decision") && Exact<LearnConfidence>(root, "confidence");
    }

    /// <summary>Some models wrap the JSON in a Markdown fence despite the schema; the fence is not part of the answer.</summary>
    private static string Unfence(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var start = trimmed.IndexOf('\n', StringComparison.Ordinal);
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (start > 0 && end > start)
            {
                return trimmed[(start + 1)..end];
            }
        }

        return trimmed;
    }
}
