using Ardea.Assessment.Matching;
using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Platform.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Assessing;

/// <summary>
/// The forward search (master plan 3.3, 7.2): an entry changed materially; which organisations does it
/// concern? A system job without organisation: it asks the two system functions «who has a component
/// under these keys» and «who has an alert for this entry» (master plan 9.2) and queues
/// <see cref="AssessAlert"/> in each of them.
/// </summary>
internal sealed record MatchEntry(Guid EntryId) : IJob
{
    public static string Kind => "assess.match-entry";

    /// <summary>One waiting job per entry; Intel already waits a minute before it tells, so a wave counts once.</summary>
    public static EnqueueOptions Options(Guid entryId) => new() { CoalesceKey = $"assess.match-entry:{entryId}" };
}

/// <summary>Computes the findings of one entry in one organisation; runs in the organisation's tenant context.</summary>
internal sealed record AssessAlert(Guid EntryId) : IJob
{
    public static string Kind => "assess.assess-alert";

    public static EnqueueOptions Options(Guid organizationId, Guid entryId) =>
        new() { OrganizationId = organizationId, CoalesceKey = $"assess.assess-alert:{organizationId}:{entryId}" };
}

/// <summary>
/// The reverse search (master plan 3.3, 7.2, 7.7): a project's profile changed; the project against the
/// entries in the lookback and against its own findings. Runs in the organisation's tenant context.
/// </summary>
internal sealed record MatchProject(Guid ProjectId) : IJob
{
    public static string Kind => "assess.match-project";

    /// <summary>The lookback of the reverse search; the same as the first load of the sources (master plan 4.10).</summary>
    public static readonly TimeSpan Lookback = TimeSpan.FromDays(180);

    public static EnqueueOptions Options(Guid organizationId, Guid projectId) =>
        new() { OrganizationId = organizationId, CoalesceKey = $"assess.match-project:{projectId}" };
}

internal sealed class MatchEntryHandler(IEntryQueries entries, IProfileQueries profiles, IProductCatalog catalog, AssessDbContext db, IJobQueue jobs)
    : IJobHandler<MatchEntry>
{
    public async Task HandleAsync(MatchEntry job, JobContext context, CancellationToken cancellationToken)
    {
        var entry = await entries.FindAsync(job.EntryId, cancellationToken);
        if (entry is null)
        {
            return;
        }

        var organizations = new SortedSet<Guid>();
        if (entry.Kind == "vulnerability" && entry.State is "published" or "disputed")
        {
            var keys = await EntryKeys.ForAsync(entry, catalog, cancellationToken);
            organizations.UnionWith(await profiles.OrganizationsWithKeysAsync(keys.All, cancellationToken));
        }

        // Organisations that already have an alert: a product may have left the entry, or the entry was withdrawn.
        organizations.UnionWith(await db.Database
            .SqlQuery<Guid>($"""SELECT organization_id AS "Value" FROM assess.organizations_with_alert({entry.Id})""")
            .ToListAsync(cancellationToken));

        foreach (var organization in organizations)
        {
            await jobs.EnqueueAsync(new AssessAlert(entry.Id), AssessAlert.Options(organization, entry.Id), cancellationToken);
        }
    }
}

internal sealed class AssessAlertHandler(IEntryQueries entries, AlertAssessor assessor, Remediations remediations) : IJobHandler<AssessAlert>
{
    public async Task HandleAsync(AssessAlert job, JobContext context, CancellationToken cancellationToken)
    {
        if (await entries.FindAsync(job.EntryId, cancellationToken) is { } entry)
        {
            var touched = await assessor.AssessAsync(entry, AssessmentScope.Organization, cancellationToken);
            await remediations.SyncAsync(touched, actor: null, cancellationToken);
        }
    }
}

internal sealed class MatchProjectHandler(
    IProfileQueries profiles,
    IEntryQueries entries,
    IProductCatalog catalog,
    AssessDbContext db,
    AlertAssessor assessor,
    Remediations remediations,
    History history,
    TimeProvider time)
    : IJobHandler<MatchProject>
{
    private const int Batch = 100;

    public async Task HandleAsync(MatchProject job, JobContext context, CancellationToken cancellationToken)
    {
        // A project that no longer exists has no components: its findings close.
        var project = await profiles.FindProjectAsync(job.ProjectId, cancellationToken)
            ?? new ProjectProfile(job.ProjectId, string.Empty, "normal", "internal", Archived: false, time.GetUtcNow(), []);

        var ids = new SortedSet<Guid>(await db.Findings.Where(f => f.ProjectId == job.ProjectId).Select(f => f.EntryId).Distinct().ToListAsync(cancellationToken));
        if (!project.Archived)
        {
            var keys = await KeysAsync(project, cancellationToken);
            ids.UnionWith(await entries.FindAffectingAsync(keys, time.GetUtcNow() - MatchProject.Lookback, cancellationToken));
        }

        // A first look at the project, or at a newly connected repository, is a first fill (master plan 7.7).
        var seen = await db.MatchScopes.Where(s => s.ProjectId == project.Id).ToListAsync(cancellationToken);
        var projectSeen = seen.Exists(s => s.RepositoryLinkId == null);
        var active = project.Archived ? [] : project.Components;
        var newLinks = active.Select(c => c.RepositoryLinkId).OfType<Guid>().Distinct()
            .Where(l => !seen.Exists(s => s.RepositoryLinkId == l)).ToHashSet();
        var backfill = active.Where(c => !projectSeen || (c.RepositoryLinkId is { } link && newLinks.Contains(link))).Select(c => c.Id).ToHashSet();

        var scope = AssessmentScope.ForProject(project, backfill);
        var touched = new HashSet<Guid>();
        foreach (var chunk in ids.Chunk(Batch))
        {
            foreach (var entry in await entries.FindManyAsync(chunk, cancellationToken))
            {
                touched.UnionWith(await assessor.AssessAsync(entry, scope, cancellationToken));
            }
        }

        await remediations.SyncAsync(touched, actor: null, cancellationToken);
        if (backfill.Count == 0)
        {
            return;
        }

        var now = time.GetUtcNow();
        if (!projectSeen)
        {
            db.MatchScopes.Add(new MatchScope { Id = Guid.CreateVersion7(), ProjectId = project.Id, MatchedAt = now });
        }

        foreach (var link in newLinks)
        {
            db.MatchScopes.Add(new MatchScope { Id = Guid.CreateVersion7(), ProjectId = project.Id, RepositoryLinkId = link, MatchedAt = now });
        }

        // One summary instead of single alarms: «first assessment for project X: 3 actions, highest level 4».
        var actions = await db.Remediations.Where(r => backfill.Contains(r.ComponentId) && r.Status != "closed")
            .Select(r => r.Level).ToListAsync(cancellationToken);
        var findings = await db.Findings.CountAsync(f => backfill.Contains(f.ComponentId) && f.IsBackfill, cancellationToken);
        history.Add(EventKinds.BackfillCompleted, new
        {
            scope = projectSeen ? "repository" : "project",
            repositoryLinks = newLinks.Order().ToList(),
            actions = actions.Count,
            highestLevel = actions.Count == 0 ? (int?)null : actions.Max(),
            findings,
        }, project: project.Id);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Every key of the project's components in the forms of <c>intel.entry_affected</c>, and the
    /// catalogue's plugins of its platforms for the weak «ecosystem» hit.
    /// </summary>
    private async Task<AffectedKeys> KeysAsync(ProjectProfile project, CancellationToken cancellationToken)
    {
        var products = new HashSet<string>(StringComparer.Ordinal);
        var cpes = new HashSet<KeyPair>();
        var packages = new HashSet<KeyPair>();
        foreach (var key in project.Components.SelectMany(c => c.StrongKeys.Concat(c.WeakKeys)))
        {
            switch (key.Type)
            {
                case "product":
                    products.Add(key.Value);
                    break;
                case "cpe" when CpePair.TryParse(key.Value, out var pair):
                    cpes.Add(new KeyPair(pair.Vendor, pair.Product));
                    break;
                case "purl" when PackageCoordinate.TryParsePurl(key.Value, out var package, out _):
                    packages.Add(new KeyPair(package.Type, package.Name));
                    break;
            }
        }

        foreach (var platform in project.Components.SelectMany(c => c.StrongKeys).Where(k => k.Type == "product").Select(k => k.Value).Distinct(StringComparer.Ordinal).ToList())
        {
            products.UnionWith((await catalog.PluginsOfAsync(platform, cancellationToken)).Select(p => p.Key));
        }

        return new AffectedKeys([.. products.Order(StringComparer.Ordinal)], [.. cpes], [.. packages]);
    }
}

/// <summary>Starts the forward search when Intel reports a material change (master plan 3.3).</summary>
internal sealed class EntryChangeSubscriber(IJobQueue jobs) : IEntryChangeSubscriber
{
    public Task OnEntryChangedAsync(EntryChanged change, CancellationToken cancellationToken) =>
        jobs.EnqueueAsync(new MatchEntry(change.EntryId), MatchEntry.Options(change.EntryId), cancellationToken);
}

/// <summary>Starts the reverse search when Assets reports a changed profile (master plan 3.3).</summary>
internal sealed class ProfileChangeSubscriber(IJobQueue jobs) : IProfileChangeSubscriber
{
    public Task OnProfileChangedAsync(ProfileChanged change, CancellationToken cancellationToken) =>
        jobs.EnqueueAsync(new MatchProject(change.ProjectId), MatchProject.Options(change.OrganizationId, change.ProjectId), cancellationToken);
}
