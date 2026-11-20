using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.RateLimiting;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Http;

internal static class SourceHttp
{
    public const string ClientName = "ardea-source";
    public const string BackfillClientName = "ardea-source-backfill";

    public static readonly ProductInfoHeaderValue UserAgent = new(
        "Ardea",
        typeof(SourceHttp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0");

    public static readonly ProductInfoHeaderValue UserAgentComment = new("(+https://github.com/NicoM77/Ardea)");
}

internal sealed class HostRateLimits : IDisposable
{
    private readonly ConcurrentDictionary<string, RateLimiter> _limiters = new(StringComparer.OrdinalIgnoreCase);

    public RateLimiter For(string host, int requestsPerMinute) =>
        _limiters.GetOrAdd(host, _ => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = Math.Max(1, requestsPerMinute / 6),
            TokensPerPeriod = Math.Max(1, requestsPerMinute / 6),
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            QueueLimit = 1000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        }));

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
        {
            limiter.Dispose();
        }
    }
}

internal sealed class SourceGuardHandler(SourceDescriptor descriptor, long maxResponseBytes, HostRateLimits limits) : DelegatingHandler
{
    private const int MaxRedirects = 5;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.Add(SourceHttp.UserAgent);
        request.Headers.UserAgent.Add(SourceHttp.UserAgentComment);

        for (var redirects = 0; ; redirects++)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request without address.");
            EnsureAllowed(uri);
            using (var lease = await limits.For(uri.Host, descriptor.RequestsPerMinute).AcquireAsync(1, cancellationToken))
            {
                if (!lease.IsAcquired)
                {
                    throw new HttpRequestException($"Rate limit for {uri.Host} exhausted.");
                }
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
            {
                if (redirects >= MaxRedirects)
                {
                    response.Dispose();
                    throw new HttpRequestException($"More than {MaxRedirects} redirects from {descriptor.Key}.");
                }

                var target = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                request = Clone(request, target);
                continue;
            }

            if (response.Content.Headers.ContentLength is { } length && length > maxResponseBytes)
            {
                response.Dispose();
                throw new HttpRequestException($"Response of {length} bytes from {uri.Host} exceeds the limit of {maxResponseBytes}.");
            }

            response.Content = new LimitedContent(response.Content, maxResponseBytes, uri.Host);
            return response;
        }
    }

    private void EnsureAllowed(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new HttpRequestException($"Source {descriptor.Key} may only use HTTPS, not {uri.Scheme}.");
        }

        if (!descriptor.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new HttpRequestException($"Source {descriptor.Key} may not call {uri.Host}; allowed: {string.Join(", ", descriptor.AllowedHosts)}.");
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static HttpRequestMessage Clone(HttpRequestMessage original, Uri target)
    {
        var copy = new HttpRequestMessage(original.Method == HttpMethod.Post ? HttpMethod.Get : original.Method, target);
        foreach (var header in original.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return copy;
    }
}

internal sealed class LimitedContent(HttpContent inner, long limit, string host) : HttpContent
{
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        await SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await using var source = await CreateContentReadStreamAsync(cancellationToken);
        await source.CopyToAsync(stream, cancellationToken);
    }

    protected override Task<Stream> CreateContentReadStreamAsync() => CreateContentReadStreamAsync(CancellationToken.None);

    protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
        new LimitedStream(await inner.ReadAsStreamAsync(cancellationToken), limit, host);

    protected override bool TryComputeLength(out long length)
    {
        length = inner.Headers.ContentLength ?? 0;
        return inner.Headers.ContentLength is not null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}

internal sealed class LimitedStream(Stream inner, long limit, string host) : Stream
{
    private long _read;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => _read; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private int Count(int read)
    {
        _read += read;
        if (_read > limit)
        {
            throw new HttpRequestException($"Response from {host} exceeds the limit of {limit} bytes.");
        }

        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
