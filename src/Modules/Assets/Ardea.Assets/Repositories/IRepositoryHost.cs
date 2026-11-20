using Ardea.Assets.GitHub;

namespace Ardea.Assets.Repositories;

internal sealed record RepositoryRef(GitHubHost Host, string? Token, string Owner, string Name, string? Ref = null, string? Subfolder = null)
{
    public override string ToString() => $"{Owner}/{Name}@{Ref ?? "(default branch)"}" + (Subfolder is null ? string.Empty : $":{Subfolder}");
}

internal enum RepositoryFailure
{
    Unauthorized,

    Forbidden,

    NotFound,

    RefNotFound,

    EmptyRepository,

    RateLimited,

    DependencyGraphDisabled,

    SbomNotReady,

    TooLarge,

    NotAFile,

    InvalidRequest,

    RedirectRefused,

    InvalidResponse,

    Unavailable,
}

internal sealed record RepositoryError(RepositoryFailure Kind, string Message, TimeSpan? RetryAfter = null);

internal sealed record RepositoryResult<T>(T? Value, RepositoryError? Error)
{
    public bool Ok => Error is null;

    public static RepositoryResult<T> Success(T value) => new(value, null);

    public static RepositoryResult<T> Fail(RepositoryFailure kind, string message, TimeSpan? retryAfter = null) =>
        new(default, new RepositoryError(kind, message, retryAfter));

    public static RepositoryResult<T> Fail(RepositoryError error) => new(default, error);
}

internal sealed record BranchHead(bool Unchanged, string? CommitSha, string? ETag);

internal sealed record RepositoryFile(string Path, long Size);

internal sealed record RepositoryTree(string CommitSha, IReadOnlyList<RepositoryFile> Files, bool Truncated);

internal interface IRepositoryHost
{
    Task<RepositoryResult<BranchHead>> GetBranchHeadAsync(RepositoryRef repository, string? etag, CancellationToken cancellationToken);

    Task<RepositoryResult<T>> GetSbomAsync<T>(RepositoryRef repository, Func<Stream, CancellationToken, Task<T>> read, CancellationToken cancellationToken);

    Task<RepositoryResult<RepositoryTree>> GetTreeAsync(RepositoryRef repository, string commitSha, CancellationToken cancellationToken);

    Task<RepositoryResult<byte[]>> GetFileAsync(RepositoryRef repository, string commitSha, string path, int maxBytes, CancellationToken cancellationToken);
}
