using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Sources.Ncsc;

internal sealed record BacsWarning
{
    [JsonPropertyName("documentId")]
    public long DocumentId { get; init; }

    [JsonPropertyName("groupId")]
    public string? GroupId { get; init; }

    [JsonPropertyName("lang")]
    public string? Lang { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("publishedAt")]
    public string? PublishedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; init; }

    [JsonPropertyName("hubPosts")]
    public List<long> HubPosts { get; init; } = [];
}

internal static partial class BacsWarningReader
{
    public const int ParserVersion = 1;

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public static string? PlainText(string? html) =>
        string.IsNullOrWhiteSpace(html) ? null : Whitespace().Replace(WebUtility.HtmlDecode(Tags().Replace(html, " ")), " ").Trim();

    public static NormalizationResult Read(string content)
    {
        BacsWarning? warning;
        try
        {
            warning = JsonSerializer.Deserialize<BacsWarning>(content, Json);
        }
        catch (JsonException e)
        {
            return NormalizationResult.Quarantine("Not valid JSON: " + e.Message);
        }

        if (warning is null || string.IsNullOrWhiteSpace(warning.Title))
        {
            return NormalizationResult.Quarantine("The warning has no title.");
        }

        if (!Uri.TryCreate(warning.Url, UriKind.Absolute, out var url))
        {
            return NormalizationResult.Quarantine("The warning has no link.");
        }

        var reference = new Reference(url) { Tags = ["warning"] };
        var references = new List<Reference> { reference };
        var cves = CveIds.Find(warning.Title, warning.Summary);
        var observation = new Observation
        {
            Kind = cves.Count == 0 ? ObservationKind.Warning : ObservationKind.Advisory,
            Title = warning.Title.Trim(),
            Summary = warning.Summary,
            Language = warning.Lang,
            PublishedAt = NcscHttp.ParseDate(warning.PublishedAt),
            ModifiedAt = NcscHttp.ParseDate(warning.UpdatedAt),
        };

        var hub = warning.HubPosts.Distinct().Order()
            .Select(id => new Identifier("NCSC-HUB", id.ToString(CultureInfo.InvariantCulture))).ToList();
        if (cves.Count == 0)
        {
            references.AddRange(warning.HubPosts.Distinct().Order().Select(id => new Reference(new Uri(NcscHubProvider.PostUrl(id))) { Tags = ["related"] }));
            return NormalizationResult.Success(observation with { References = references });
        }

        return NormalizationResult.Success(observation with
        {
            References = references,
            Claims =
            [
                .. cves.Select(cve => new ClaimBlock
                {
                    Aliases = [new Identifier("CVE", cve)],
                    Related = hub,
                    References = [reference],
                }),
            ],
        });
    }
}
