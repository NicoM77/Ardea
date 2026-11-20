using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Ardea.Catalog.Contracts;
using Ardea.Catalog.Products;
using Microsoft.Extensions.AI;

namespace Ardea.Catalog.Learning;

internal sealed record LearnCandidate(string Key, string Name, string? Vendor, string Kind, IReadOnlyList<string> Cpe);

internal sealed record LearnUnknown(string? Vendor, string Product, IReadOnlyList<string> Cpe, string? Purl, int Records, IReadOnlyList<string> Titles);

internal sealed record LearnInput(LearnUnknown Unknown, IReadOnlyList<LearnCandidate> Candidates)
{
    public const int CandidateCount = 5;
    public const int TitleCount = 3;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    [JsonPropertyName("kinds")]
    public IReadOnlyList<string> Kinds => ProductDrafts.Kinds;

    [JsonPropertyName("version_schemes")]
    public IReadOnlyList<string> VersionSchemes => ProductDrafts.Schemes;

    [JsonIgnore]
    public string RawKey { get; init; } = string.Empty;

    [JsonIgnore]
    public bool Suspicious { get; init; }

    public string ToDataBlock() => "<source_data>\n" + JsonSerializer.Serialize(this, Json) + "\n</source_data>";

    public static LearnInput Build(UnknownProductStatement statement, IReadOnlyList<string> titles, CatalogLayers catalog)
    {
        var words = statement.Product.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var queries = new[] { statement.Product, $"{statement.Vendor} {statement.Product}" }
            .Concat(statement.CpePairs.Select(c => c.Replace(':', ' ').Replace('_', ' ')))
            .Concat(RuleMapping.Variants(statement.Vendor, statement.Product))
            .Concat(Enumerable.Range(1, Math.Max(words.Length - 1, 0)).Reverse().Select(n => string.Join(' ', words.Take(n))));
        var candidates = new List<CatalogProduct>();
        foreach (var query in queries.Where(q => !string.IsNullOrWhiteSpace(q)).Distinct(StringComparer.Ordinal))
        {
            foreach (var product in catalog.All.Search(query, CandidateCount))
            {
                if (candidates.Count < CandidateCount && !candidates.Exists(c => c.Key == product.Key))
                {
                    candidates.Add(product);
                }
            }
        }

        return new LearnInput(
            new LearnUnknown(statement.Vendor, statement.Product, statement.CpePairs, statement.Purl, statement.RecordCount, [.. titles.Take(TitleCount).Select(t => t.Length <= 300 ? t : t[..300])]),
            [.. candidates.Select(c => new LearnCandidate(c.Key, c.Name, c.Vendor, c.Kind.ToString().ToLowerInvariant(), [.. c.CpePairs.Select(p => p.ToString())]))])
        {
            RawKey = statement.RawKey,
            Suspicious = ProductDrafts.Suspicious(statement.Vendor, statement.Product)
                || titles.Concat(statement.CpePairs).Append(statement.Purl).Any(ProductDrafts.SuspiciousText),
        };
    }
}

internal enum LearnDecision
{
    Alias,
    New,
    Unclear,
}

internal enum LearnConfidence
{
    High,
    Medium,
    Low,
}

internal sealed record LearnAnswer(
    LearnDecision Decision,
    [property: Description("A key from candidates for alias, else null.")] string? AliasOf,
    [property: Description("The product for new, else null.")] LearnProduct? NewProduct,
    LearnConfidence Confidence,
    [property: Description("One sentence, at most 300 characters.")] string Reasoning)
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };
}

internal sealed record LearnProduct(
    [property: Description("The usual name, without version numbers.")] string Name,
    string? Vendor,
    [property: Description("One of kinds.")] string Kind,
    [property: Description("One of version_schemes.")] string VersionScheme,
    [property: Description("CPE pairs from unknown.cpe only.")] IReadOnlyList<string> Cpe,
    [property: Description("Keys of related candidates.")] IReadOnlyList<string> Related,
    [property: Description("For a plugin: the key of the candidate it runs on, else null.")] string? Platform);

internal static class LearnPrompt
{
    public const string Version = "1";

    public static readonly string System = Load(Version);

    public static IReadOnlyList<ChatMessage> Messages(LearnInput input) =>
    [
        new(ChatRole.System, System),
        new(ChatRole.User, $"Decide what the unknown product in the data block is.\n\n{input.ToDataBlock()}"),
    ];

    private static string Load(string version)
    {
        using var stream = typeof(LearnPrompt).Assembly.GetManifestResourceStream($"Ardea.Catalog.Learning.Prompts.learn-{version}.md")
            ?? throw new InvalidOperationException($"The prompt learn-{version}.md is missing from the module.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
