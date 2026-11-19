using Ardea.Assets.GitHub;

namespace Ardea.Assets.Repositories;

/// <summary>
/// A repository to read: the host and token of its access, owner and name, the branch (<c>null</c> is
/// the default branch) and, in a monorepo, the subfolder that counts (master plan 16.2, 16.3).
/// </summary>
internal sealed record RepositoryRef(GitHubHost Host, string? Token, string Owner, string Name, string? Ref = null, string? Subfolder = null)
{
    /// <summary>Never prints the token (master plan 16.9: never logged).</summary>
    public override string ToString() => $"{Owner}/{Name}@{Ref ?? "(default branch)"}" + (Subfolder is null ? string.Empty : $":{Subfolder}");
}

/// <summary>Why a call to the repository host gave no result. Failures are results, not exceptions.</summary>
internal enum RepositoryFailure
{
    /// <summary>The token is not accepted (401).</summary>
    Unauthorized,

    /// <summary>The token may not read this (403 without a rate limit).</summary>
    Forbidden,

    /// <summary>The repository does not exist or the token cannot see it (404).</summary>
    NotFound,

    /// <summary>The branch does not exist (422 for a ref without a commit).</summary>
    RefNotFound,

    /// <summary>The repository has no commit yet (409).</summary>
    EmptyRepository,

    /// <summary>A primary or secondary rate limit; <see cref="RepositoryError.RetryAfter"/> says how long to wait.</summary>
    RateLimited,

    /// <summary>The repository answers, the SBOM request answers 404 (ADR 032, decision 5).</summary>
    DependencyGraphDisabled,

    /// <summary>GitHub is still building the SBOM after the polling budget; try again later. Never an empty SBOM.</summary>
    SbomNotReady,

    /// <summary>A response is larger than its limit.</summary>
    TooLarge,

    /// <summary>The path exists but is no file (a directory, for example).</summary>
    NotAFile,

    /// <summary>An argument is not acceptable: a malformed owner, branch, commit or path.</summary>
    InvalidRequest,

    /// <summary>A redirect to a host that is not declared (16.9), or a redirect where none is expected.</summary>
    RedirectRefused,

    /// <summary>The host answered something Ardea does not understand.</summary>
    InvalidResponse,

    /// <summary>A network failure, a timeout or a server error (5xx).</summary>
    Unavailable,
}

internal sealed record RepositoryError(RepositoryFailure Kind, string Message, TimeSpan? RetryAfter = null);

/// <summary>A value or the reason there is none.</summary>
internal sealed record RepositoryResult<T>(T? Value, RepositoryError? Error)
{
    public bool Ok => Error is null;

    public static RepositoryResult<T> Success(T value) => new(value, null);

    public static RepositoryResult<T> Fail(RepositoryFailure kind, string message, TimeSpan? retryAfter = null) =>
        new(default, new RepositoryError(kind, message, retryAfter));

    public static RepositoryResult<T> Fail(RepositoryError error) => new(default, error);
}

/// <summary>
/// The head of the branch. <see cref="Unchanged"/> when the stored ETag still matches: no new SHA, and
/// on github.com with a token the answer does not count against the rate limit (16.6).
/// </summary>
internal sealed record BranchHead(bool Unchanged, string? CommitSha, string? ETag);

/// <summary>A file of the tree: path from the repository root and size in bytes.</summary>
internal sealed record RepositoryFile(string Path, long Size);

/// <summary>
/// The files of a commit within the subfolder. <see cref="Truncated"/> when GitHub cut the tree or the
/// entry limit was reached: then not every file is listed, and nothing may be concluded from a missing one.
/// </summary>
internal sealed record RepositoryTree(string CommitSha, IReadOnlyList<RepositoryFile> Files, bool Truncated);

/// <summary>
/// Read access to a repository host (master plan 16.2, 16.4). GitHub today; GitLab, Azure DevOps and
/// Bitbucket later behind the same interface. Only reads, never executes; every answer is bounded.
/// </summary>
internal interface IRepositoryHost
{
    /// <summary>
    /// The commit at the head of the branch, as a conditional request: with the <paramref name="etag"/>
    /// of the last answer, an unchanged branch gives <see cref="BranchHead.Unchanged"/>.
    /// </summary>
    Task<RepositoryResult<BranchHead>> GetBranchHeadAsync(RepositoryRef repository, string? etag, CancellationToken cancellationToken);

    /// <summary>
    /// The SBOM of the dependency graph (SPDX JSON, ADR 032): requested, polled until ready within a bounded
    /// time, downloaded from a declared host and handed to <paramref name="read"/> as a stream that fails
    /// beyond the size limit. The SBOM always describes the default branch and the whole repository.
    /// </summary>
    Task<RepositoryResult<T>> GetSbomAsync<T>(RepositoryRef repository, Func<Stream, CancellationToken, Task<T>> read, CancellationToken cancellationToken);

    /// <summary>The files of <paramref name="commitSha"/> under the subfolder of <paramref name="repository"/>, without symbolic links and submodules.</summary>
    Task<RepositoryResult<RepositoryTree>> GetTreeAsync(RepositoryRef repository, string commitSha, CancellationToken cancellationToken);

    /// <summary>The raw content of one file at <paramref name="commitSha"/>, at most <paramref name="maxBytes"/> long.</summary>
    Task<RepositoryResult<byte[]>> GetFileAsync(RepositoryRef repository, string commitSha, string path, int maxBytes, CancellationToken cancellationToken);
}
