using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ardea.Assets.Features;
using Ardea.Assets.GitHub;
using Ardea.Assets.Persistence;
using Ardea.Assets.Repositories.Detection;
using Ardea.Assets.Repositories.Packages;
using Ardea.Catalog.Contracts;
using Ardea.Platform.Audit;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ardea.Assets.Repositories.Indexing;

/// <summary>Reads one linked repository into its project's profile (master plan 3.3, 16.2; task 2.13).</summary>
internal sealed record IndexRepository(Guid LinkId, bool ApplyShrink = false) : IJob
{
    public static string Kind => "assets.index-repository";

    /// <summary>Default time between two readings of a link (master plan 16.6).</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(60);

    /// <summary>After this long failing, <c>repository.unavailable</c> is raised once (master plan 16.6).</summary>
    public static readonly TimeSpan UnavailableAfter = TimeSpan.FromDays(3);

    /// <summary>Files read per run and their size (master plan 16.3).</summary>
    public const int MaxFiles = 50;

    public const int MaxFileBytes = 1024 * 1024;

    /// <summary>Below this many packages a shrink is not held back: small lists change a lot by nature.</summary>
    public const int ShrinkFloor = 10;

    public static EnqueueOptions Options(Guid linkId, Guid organizationId, TimeSpan? delay = null) =>
        new() { CoalesceKey = $"assets.index-repository:{linkId}", OrganizationId = organizationId, Delay = delay ?? TimeSpan.Zero };
}

/// <summary>
/// Reads in a short unit of work, talks to GitHub without an open transaction, and writes the result
/// in the job's own unit of work (master plan 9.2). The same commit gives the same profile, a failed
/// reading never removes anything, and a much smaller state waits for confirmation (16.5).
/// </summary>
internal sealed partial class IndexRepositoryHandler(
    IUnitOfWorkFactory units,
    AssetsDbContext db,
    IRepositoryHost host,
    IProductCatalog catalog,
    IVersionSchemes schemes,
    IAuditLog audit,
    AuditOrigin origin,
    IJobQueue jobs,
    TimeProvider time,
    ILogger<IndexRepositoryHandler> logger) : IJobHandler<IndexRepository>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(IndexRepository job, JobContext context, CancellationToken cancellationToken)
    {
        var repository = await ReadRepositoryAsync(job.LinkId, cancellationToken);
        if (repository is null)
        {
            return;
        }

        var (link, reference) = repository.Value;
        var now = time.GetUtcNow();

        if (job.ApplyShrink)
        {
            await ApplyHeldBackAsync(link.Id, now, cancellationToken);
            return;
        }

        var head = await host.GetBranchHeadAsync(reference, link.HeadEtag, cancellationToken);
        if (!head.Ok)
        {
            await FailAsync(link.Id, head.Error!, now, cancellationToken);
            return;
        }

        if (head.Value!.Unchanged || head.Value.CommitSha == link.LastCommitSha)
        {
            // The same commit gives the same profile: nothing to read.
            await TouchAsync(link.Id, head.Value.ETag ?? link.HeadEtag, now, cancellationToken);
            return;
        }

        var sha = head.Value.CommitSha!;
        var reading = await ReadCommitAsync(reference, sha, link, cancellationToken);
        if (reading.Error is { } error)
        {
            await FailAsync(link.Id, error, now, cancellationToken);
            return;
        }

        await WriteAsync(link.Id, sha, head.Value.ETag, reading, now, cancellationToken);
    }

    /// <summary>The link and how to reach it; <c>null</c> when the link or its access is gone.</summary>
    private async Task<(RepositoryLink Link, RepositoryRef Reference)?> ReadRepositoryAsync(Guid linkId, CancellationToken cancellationToken)
    {
        await using var scope = units.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        var link = await reader.RepositoryLinks.AsNoTracking().SingleOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        var connection = link is null ? null : await reader.RepositoryConnections.AsNoTracking().SingleOrDefaultAsync(c => c.Id == link.ConnectionId, cancellationToken);
        if (link is null || connection is null)
        {
            return null;
        }

        var gitHost = await scope.ServiceProvider.GetRequiredService<GitHubHosts>().ResolveAsync(connection.HostId, cancellationToken)
            ?? new GitHubHost(connection.HostId, new Uri("https://removed.invalid/"));
        var token = connection.TokenEnc is null ? null : scope.ServiceProvider.GetRequiredService<ISecretProtector>().Unprotect(ConnectionEndpoints.TokenPurpose, connection.TokenEnc);
        return (link, new RepositoryRef(gitHost, token, link.Owner, link.Name, link.Ref, link.Path));
    }

    private sealed record Reading(
        IReadOnlyList<PackageFinding> Packages,
        IReadOnlyList<PlatformSuggestion> Suggestions,
        string State,
        string? Reason,
        byte[]? Sbom,
        IReadOnlyList<string> FilesRead,
        RepositoryError? Error);

    /// <summary>File tree, the known files (at most 50, each at most 1 MB), the SBOM where it fits, readers and detection rules.</summary>
    private async Task<Reading> ReadCommitAsync(RepositoryRef reference, string sha, RepositoryLink link, CancellationToken cancellationToken)
    {
        var tree = await host.GetTreeAsync(reference, sha, cancellationToken);
        if (!tree.Ok)
        {
            return new Reading([], [], "failing", null, null, [], tree.Error);
        }

        var candidates = tree.Value!.Files
            .Where(f => f.Size <= IndexRepository.MaxFileBytes && (IsLockfile(f.Path) || PlatformDetection.IsCandidate(f.Path)))
            .OrderBy(f => IsLockfile(f.Path) ? 0 : 1)
            .ThenBy(f => f.Path.Count(c => c == '/'))
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .Take(IndexRepository.MaxFiles)
            .ToList();

        var lockfileFindings = new List<PackageFinding>();
        var platformFiles = new List<FileContent>();
        var read = new List<string>();
        foreach (var file in candidates)
        {
            var content = await host.GetFileAsync(reference, sha, file.Path, IndexRepository.MaxFileBytes, cancellationToken);
            if (!content.Ok)
            {
                if (content.Error!.Kind is RepositoryFailure.RateLimited or RepositoryFailure.Unauthorized or RepositoryFailure.Forbidden)
                {
                    return new Reading([], [], "failing", null, null, [], content.Error);
                }

                continue;
            }

            read.Add(file.Path);
            var name = file.Path[(file.Path.LastIndexOf('/') + 1)..];
            if (name == "package-lock.json")
            {
                lockfileFindings.AddRange(NpmLockReader.Read(content.Value, file.Path).Findings);
            }
            else if (name == "packages.lock.json")
            {
                lockfileFindings.AddRange(NuGetLockReader.Read(content.Value, file.Path).Findings);
            }
            else
            {
                platformFiles.Add(new FileContent(file.Path, Encoding.UTF8.GetString(content.Value!)));
            }
        }

        // The SBOM describes the default branch of the whole repository (ADR 032); for another branch or a
        // subfolder only the own readers count.
        IReadOnlyList<PackageFinding> sbomFindings = [];
        byte[]? sbomBytes = null;
        var (state, reason) = ("ok", (string?)null);
        if (link.Ref is null && link.Path is null)
        {
            var sbom = await host.GetSbomAsync(reference, ReadSbomAsync, cancellationToken);
            if (sbom.Ok)
            {
                (sbomFindings, sbomBytes) = (sbom.Value!.Result.Findings, sbom.Value.Compressed);
            }
            else if (sbom.Error!.Kind is RepositoryFailure.SbomNotReady or RepositoryFailure.RateLimited or RepositoryFailure.Unavailable)
            {
                return new Reading([], [], "failing", null, null, [], sbom.Error);
            }
            else
            {
                (state, reason) = ("partial", sbom.Error.Kind == RepositoryFailure.DependencyGraphDisabled
                    ? "The dependency graph is not enabled for this repository; only lock files and platform files were read."
                    : $"No SBOM: {sbom.Error.Message}");
            }
        }

        var packages = PackageMerge.Merge(sbomFindings, lockfileFindings);
        var suggestions = await PlatformDetection.DetectAsync(platformFiles, catalog, cancellationToken);
        return new Reading(packages, suggestions, state, reason, sbomBytes, read, null);
    }

    private sealed record SbomRead(SbomReadResult Result, byte[] Compressed);

    /// <summary>Keeps the SBOM compressed for the link (master plan 4.5) and reads it.</summary>
    private static async Task<SbomRead> ReadSbomAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        var result = await SpdxReader.ReadAsync(buffer, cancellationToken);
        return new SbomRead(result, Compress(buffer.ToArray()));
    }

    /// <summary>The job's unit of work: components, suggestions, link state, audit and the reverse search together.</summary>
    private async Task WriteAsync(Guid linkId, string sha, string? etag, Reading reading, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var link = await db.RepositoryLinks.SingleOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        if (link is null)
        {
            return;
        }

        origin.Origin = $"repository:{link.Owner}/{link.Name}@{sha}";
        var existing = await db.Components.Where(c => c.RepositoryLinkId == linkId).ToListAsync(cancellationToken);
        var packageCount = existing.Count(c => c.Purl is not null);
        var first = link.LastIndexedAt is null;

        // A much smaller state waits for a person (16.5): a switched-off graph or an empty SBOM never empties a profile.
        if (!first && packageCount >= IndexRepository.ShrinkFloor && reading.Packages.Count < packageCount / 2.0)
        {
            link.PendingShrink = Compress(JsonSerializer.SerializeToUtf8Bytes(reading.Packages.Select(Stored.From), Json));
            link.PendingShrinkCommit = sha;
            Settle(link, reading.State, reading.Reason, sha, etag, now);
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(new AuditEntry("repository.shrink_held", "repository_link", linkId.ToString(),
                new { commit = sha, before = packageCount, after = reading.Packages.Count }), cancellationToken);
            LogShrinkHeld(link.Owner, link.Name, packageCount, reading.Packages.Count);
            return;
        }

        var changed = await ApplyPackagesAsync(link, existing, reading.Packages, now, cancellationToken);
        await ApplySuggestionsAsync(link, reading.Suggestions, now, cancellationToken);
        (link.PendingShrink, link.PendingShrinkCommit) = (null, null);
        link.LastSbom = reading.Sbom ?? link.LastSbom;
        Settle(link, reading.State, reading.Reason, sha, etag, now);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("repository.indexed", "repository_link", linkId.ToString(), new
        {
            commit = sha,
            first,
            state = reading.State,
            files = reading.FilesRead.Count,
            packages = reading.Packages.Count,
            created = changed.Created.Count,
            updated = changed.Updated.Count,
            removed = changed.Removed.Count,
            suggestions = reading.Suggestions.Count,
        }), cancellationToken);
        if (changed.Any)
        {
            await jobs.EnqueueAsync(new ProjectProfileChanged(link.ProjectId), ProjectProfileChanged.Options(link.ProjectId), cancellationToken);
        }
    }

    /// <summary>Applies a held-back state after a person confirmed it (<c>confirm-shrink</c>).</summary>
    private async Task ApplyHeldBackAsync(Guid linkId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var link = await db.RepositoryLinks.SingleOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        if (link?.PendingShrink is null)
        {
            return;
        }

        origin.Origin = $"repository:{link.Owner}/{link.Name}@{link.PendingShrinkCommit}";
        var stored = JsonSerializer.Deserialize<List<Stored>>(Decompress(link.PendingShrink), Json) ?? [];
        var existing = await db.Components.Where(c => c.RepositoryLinkId == linkId).ToListAsync(cancellationToken);
        var changed = await ApplyPackagesAsync(link, existing, [.. stored.Select(s => s.ToFinding())], now, cancellationToken);
        (link.PendingShrink, link.PendingShrinkCommit) = (null, null);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("repository.shrink_applied", "repository_link", linkId.ToString(),
            new { created = changed.Created.Count, updated = changed.Updated.Count, removed = changed.Removed.Count }), cancellationToken);
        if (changed.Any)
        {
            await jobs.EnqueueAsync(new ProjectProfileChanged(link.ProjectId), ProjectProfileChanged.Options(link.ProjectId), cancellationToken);
        }
    }

    private async Task<PackageChanges> ApplyPackagesAsync(RepositoryLink link, List<Component> existing, IReadOnlyList<PackageFinding> packages, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var changes = PackageReconciler.Reconcile(existing, packages);
        foreach (var (component, finding) in changes.Created)
        {
            (component.ProjectId, component.RepositoryLinkId, component.CreatedAt) = (link.ProjectId, link.Id, now);
            await DescribeAsync(component, finding, now, cancellationToken);
            db.Components.Add(component);
        }

        foreach (var (component, finding) in changes.Updated)
        {
            await DescribeAsync(component, finding, now, cancellationToken);
        }

        db.Components.RemoveRange(changes.Removed);
        await db.SaveChangesAsync(cancellationToken);
        await ComponentKeys.RefreshAsync(db, catalog, [.. changes.Created.Select(c => c.Component), .. changes.Updated.Select(c => c.Component)], cancellationToken);
        return changes;
    }

    /// <summary>Package, version and evidence of a component from its finding; the catalogue names the product where it knows the package.</summary>
    private async Task DescribeAsync(Component component, PackageFinding finding, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var product = await catalog.FindByPackageAsync(finding.Package.Type, finding.Package.Name, cancellationToken);
        component.ProductKey = product?.Key;
        component.CustomName = product is null ? Cut(finding.Package.Name, 200) : null;
        component.Kind = product?.Kind.ToString().ToLowerInvariant() ?? "library";
        component.Version = string.IsNullOrWhiteSpace(finding.Version) ? null : Cut(finding.Version.Trim(), 100);
        component.VersionPrecision = VersionPrecisions.Of(component.Version, finding.Purl, component.Origin);
        component.VersionScheme = product?.VersionScheme ?? schemes.ForEcosystem(finding.Package.Type)?.Name;
        component.Purl = Cut(finding.Purl, 500);
        component.DependencyScope = PackageReconciler.Scope(finding);
        component.DependencyRelation = PackageReconciler.Relation(finding);
        component.Evidence = JsonSerializer.SerializeToDocument(
            new[] { new { file = finding.Evidence.Path ?? "(dependency graph)", line = finding.Evidence.Line ?? 0, value = finding.Purl } }, Json);
        component.ChangedAt = now;
    }

    /// <summary>
    /// Suggestions keep their decision: one already confirmed or dismissed for the same values is not
    /// made again; open ones the repository no longer gives are dropped.
    /// </summary>
    private async Task ApplySuggestionsAsync(RepositoryLink link, IReadOnlyList<PlatformSuggestion> suggestions, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var known = await db.ComponentSuggestions.Where(s => s.RepositoryLinkId == link.Id).ToListAsync(cancellationToken);
        var current = new HashSet<string>(StringComparer.Ordinal);
        foreach (var suggestion in suggestions)
        {
            var fingerprint = Fingerprint(suggestion);
            current.Add(fingerprint);
            if (known.Any(k => k.Fingerprint == fingerprint))
            {
                continue;
            }

            db.ComponentSuggestions.Add(new ComponentSuggestion
            {
                Id = Guid.CreateVersion7(),
                ProjectId = link.ProjectId,
                RepositoryLinkId = link.Id,
                ProductKey = suggestion.ProductKey,
                Version = suggestion.VersionLine,
                Evidence = JsonSerializer.SerializeToDocument(suggestion.Evidence.Select(e => new { file = e.File, line = e.Line, value = e.Value }), Json),
                Fingerprint = fingerprint,
                CreatedAt = now,
            });
        }

        db.ComponentSuggestions.RemoveRange(known.Where(k => k.State == "open" && !current.Contains(k.Fingerprint)));
    }

    private static void Settle(RepositoryLink link, string state, string? reason, string sha, string? etag, DateTimeOffset now)
    {
        (link.State, link.StateReason, link.FailingSince, link.UnavailableRaisedAt) = (state, reason, null, null);
        (link.LastCommitSha, link.HeadEtag, link.LastIndexedAt) = (sha, etag, now);
        link.NextCheckAt = NextCheck(link.Id, now);
    }

    private async Task TouchAsync(Guid linkId, string? etag, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var link = await db.RepositoryLinks.SingleOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        if (link is not null)
        {
            (link.HeadEtag, link.NextCheckAt) = (etag, NextCheck(link.Id, now));
            if (link.State == "failing")
            {
                (link.State, link.StateReason, link.FailingSince, link.UnavailableRaisedAt) = (link.LastIndexedAt is null ? "pending" : "ok", null, null, null);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// A failed reading changes nothing in the profile (16.5). Passing trouble (rate limit, GitHub
    /// unavailable, SBOM still being built) only moves the due time; lasting trouble marks the link
    /// failing, and after three days raises <c>repository.unavailable</c> once (16.6).
    /// </summary>
    private async Task FailAsync(Guid linkId, RepositoryError error, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var link = await db.RepositoryLinks.SingleOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        if (link is null)
        {
            return;
        }

        if (error.Kind is RepositoryFailure.RateLimited or RepositoryFailure.Unavailable or RepositoryFailure.SbomNotReady)
        {
            link.NextCheckAt = now + (error.RetryAfter ?? TimeSpan.FromMinutes(15));
            await db.SaveChangesAsync(cancellationToken);
            if (error.Kind == RepositoryFailure.SbomNotReady)
            {
                // The report is being built; ask again soon rather than in an hour.
                await jobs.EnqueueAsync(new IndexRepository(linkId), IndexRepository.Options(linkId, link.OrganizationId, error.RetryAfter ?? TimeSpan.FromMinutes(2)), cancellationToken);
            }

            return;
        }

        link.State = "failing";
        link.StateReason = Cut(error.Message, 500);
        link.FailingSince ??= now;
        link.NextCheckAt = NextCheck(link.Id, now);
        if (link.UnavailableRaisedAt is null && now - link.FailingSince >= IndexRepository.UnavailableAfter)
        {
            link.UnavailableRaisedAt = now;
            // The notification rules of phase 5 pick this event up; until then it is in the audit log.
            await audit.RecordAsync(new AuditEntry("repository.unavailable", "repository_link", linkId.ToString(),
                new { link.Owner, link.Name, since = link.FailingSince, reason = link.StateReason }), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        LogFailing(link.Owner, link.Name, error.Kind.ToString());
    }

    /// <summary>Every link once an hour, spread over the hour by its id so that they do not all come at once.</summary>
    private static DateTimeOffset NextCheck(Guid linkId, DateTimeOffset now) =>
        now + IndexRepository.Interval + TimeSpan.FromSeconds(Math.Abs(linkId.GetHashCode()) % 600);

    private static bool IsLockfile(string path) =>
        path.EndsWith("/package-lock.json", StringComparison.Ordinal) || path == "package-lock.json"
        || path.EndsWith("/packages.lock.json", StringComparison.Ordinal) || path == "packages.lock.json";

    private static string Fingerprint(PlatformSuggestion s) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', [s.ProductKey, s.VersionLine ?? string.Empty, .. s.Evidence.Select(e => $"{e.File}\t{e.Value}").Order(StringComparer.Ordinal)]))));

    private static string Cut(string value, int length) => value.Length <= length ? value : value[..length];

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>A finding as kept for a held-back state.</summary>
    private sealed record Stored(string Type, string Name, string? Version, bool Exact, DependencyScope Scope, DependencyRelation Relation, string? Path, int? Line, FindingSource Source)
    {
        public static Stored From(PackageFinding f) =>
            new(f.Package.Type, f.Package.Name, f.Version, f.Exact, f.Scope, f.Relation, f.Evidence.Path, f.Evidence.Line, f.Evidence.Source);

        public PackageFinding ToFinding() =>
            new(new PackageCoordinate(Type, Name), Version, Exact, Scope, Relation, new FindingEvidence(Source, Path, Line));
    }

    [LoggerMessage(EventId = 5101, Level = LogLevel.Warning, Message = "Repository {Owner}/{Name}: new state with {After} packages instead of {Before} held back for confirmation")]
    private partial void LogShrinkHeld(string owner, string name, int before, int after);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Warning, Message = "Repository {Owner}/{Name} failing: {Kind}")]
    private partial void LogFailing(string owner, string name, string kind);
}

/// <summary>Every five minutes: which links are due, across organisations, through the system function (master plan 9.2).</summary>
internal sealed record ScheduleDueRepositories : IJob
{
    public static string Kind => "assets.schedule-due-repositories";
}

internal sealed class ScheduleDueRepositoriesHandler(IUnitOfWork unitOfWork, IJobQueue jobs) : IJobHandler<ScheduleDueRepositories>
{
    public async Task HandleAsync(ScheduleDueRepositories job, JobContext context, CancellationToken cancellationToken)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        var due = new List<(Guid Organization, Guid Link)>();
        await using (var command = new Npgsql.NpgsqlCommand("SELECT organization_id, link_id FROM assets.claim_due_repository_links(200, interval '30 minutes')", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                due.Add((reader.GetGuid(0), reader.GetGuid(1)));
            }
        }

        foreach (var (organization, link) in due)
        {
            await jobs.EnqueueAsync(new IndexRepository(link), IndexRepository.Options(link, organization), cancellationToken);
        }
    }
}
