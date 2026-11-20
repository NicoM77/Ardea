using System.Globalization;
using System.Net;
using System.Text.Json;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Sources.Ncsc;

internal static class NcscHttp
{
    public static async Task<string?> GetJsonAsync(SyncContext context, Uri uri, CancellationToken cancellationToken)
    {
        using var response = await context.Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        ConditionalRequests.Hint(context, response);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{uri.Host} answered {mediaType ?? "without a content type"} instead of JSON; the interface may have changed.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public static JsonDocument Parse(string json, Uri uri)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"The response of {uri} is not valid JSON.", e);
        }
    }

    public static string? String(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static DateTimeOffset? Date(this JsonElement element, string name) =>
        element.String(name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    public static DateTimeOffset? ParseDate(string? text) =>
        text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
}
