namespace Ardea.Assets.Repositories;

/// <summary>Thrown by <see cref="BoundedStream"/> when more than its limit is read.</summary>
internal sealed class ContentTooLargeException(long limit) : IOException($"The content is larger than {limit} bytes.")
{
    public long Limit { get; } = limit;
}

/// <summary>
/// A read-only view of a stream that fails as soon as more than <c>limit</c> bytes come through, so a
/// limit holds while reading and not only after everything is in memory (master plan 16.9).
/// </summary>
internal sealed class BoundedStream(Stream inner, long limit) : Stream
{
    private long _read;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _read;
        set => throw new NotSupportedException();
    }

    /// <summary>Everything from <paramref name="stream"/>, or <c>null</c> when it is longer than <paramref name="limit"/>.</summary>
    public static async Task<byte[]?> ReadAllAsync(Stream stream, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        try
        {
            await using var bounded = new BoundedStream(stream, limit);
            await bounded.CopyToAsync(buffer, 81920, cancellationToken);
        }
        catch (ContentTooLargeException)
        {
            return null;
        }

        return buffer.ToArray();
    }

    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Count(int bytes)
    {
        _read += bytes;
        if (_read > limit)
        {
            throw new ContentTooLargeException(limit);
        }

        return bytes;
    }
}
