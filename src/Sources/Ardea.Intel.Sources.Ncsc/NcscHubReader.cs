using System.Text.Json;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Sources.Ncsc;

internal static class NcscHubReader
{
    public const int ParserVersion = 1;

    public static NormalizationResult Read(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            return NormalizationResult.Quarantine("Not valid JSON: " + e.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
            {
                return NormalizationResult.Quarantine("The post has no numeric id.");
            }

            var title = root.String("title");
            if (string.IsNullOrWhiteSpace(title))
            {
                return NormalizationResult.Quarantine("The post has no title.");
            }

            if (root.String("tlpStatus") is { } tlp && !string.Equals(tlp, "Clear", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizationResult.Quarantine($"TLP marking is {tlp}, not Clear.");
            }

            var summary = root.String("summary");
            var text = root.String("content");
            var url = new Uri(NcscHubProvider.PostUrl(id));
            var reference = new Reference(url) { Tags = ["advisory"] };
            var created = root.Date("created");
            var modified = root.Date("lastModified") ?? created;

            var claims = CveIds.Find(title, summary, text)
                .Select(cve => new ClaimBlock
                {
                    Aliases = [new Identifier("CVE", cve)],
                    Related = [new Identifier("NCSC-HUB", id.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                    References = [reference],
                })
                .ToList();

            return NormalizationResult.Success(new Observation
            {
                Kind = ObservationKind.Advisory,
                Title = title.Trim(),
                Summary = summary?.Trim(),
                Description = text,
                Language = "en",
                PublishedAt = created,
                ModifiedAt = modified,
                Claims = claims,
                References = [reference],
            });
        }
    }
}
