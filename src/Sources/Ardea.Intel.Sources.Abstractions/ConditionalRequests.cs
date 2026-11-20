using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Ardea.Intel.Sources.Abstractions;

public sealed record HttpValidators(string? ETag, DateTimeOffset? LastModified)
{
    public static readonly HttpValidators None = new(null, null);

    public override string ToString() =>
        $"{ETag}|{LastModified?.ToString("O", CultureInfo.InvariantCulture)}";

    public static HttpValidators Parse(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return None;
        }

        var parts = value.Split('|', 2);
        return new HttpValidators(
            parts[0].Length > 0 ? parts[0] : null,
            parts.Length > 1 && DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastModified) ? lastModified : null);
    }
}

public sealed class ConditionalResponse : IDisposable
{
    private ConditionalResponse(HttpResponseMessage? response, HttpValidators validators)
    {
        Response = response;
        Validators = validators;
    }

    public HttpResponseMessage? Response { get; }

    public HttpValidators Validators { get; }

    public bool NotModified => Response is null;

    internal static ConditionalResponse Unchanged(HttpValidators validators) => new(null, validators);

    internal static ConditionalResponse Changed(HttpResponseMessage response) => new(
        response,
        new HttpValidators(response.Headers.ETag?.ToString(), response.Content.Headers.LastModified));

    public void Dispose() => Response?.Dispose();
}

public static class ConditionalRequests
{
    public static async Task<ConditionalResponse> GetIfChangedAsync(
        this SyncContext context, Uri uri, HttpValidators validators, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (validators.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var tag))
        {
            request.Headers.IfNoneMatch.Add(tag);
        }

        if (validators.LastModified is { } lastModified)
        {
            request.Headers.IfModifiedSince = lastModified;
        }

        var response = await context.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        Hint(context, response);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            response.Dispose();
            return ConditionalResponse.Unchanged(validators);
        }

        response.EnsureSuccessStatusCode();
        return ConditionalResponse.Changed(response);
    }

    public static void Hint(SyncContext context, HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { } retry)
        {
            var delay = retry.Delta ?? (retry.Date is { } date ? date - context.Clock.GetUtcNow() : TimeSpan.Zero);
            context.SuggestNextSync(delay);
        }

        if (response.Headers.CacheControl?.MaxAge is { } maxAge)
        {
            context.SuggestNextSync(maxAge);
        }
    }
}
