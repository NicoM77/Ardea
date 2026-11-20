using Ardea.Assessment.Matching;
using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Platform.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Assessment.Assessing;

internal sealed record MatchEntry(Guid EntryId) : IJob
{
    public static string Kind => "assess.match-entry";

    public static EnqueueOptions Options(Guid entryId) => new() { CoalesceKey = $"assess.match-entry:{entryId}" };
}

internal sealed record AssessAlert(Guid EntryId) : IJob
{
    public static string Kind => "assess.assess-alert";

    public static EnqueueOptions Options(Guid organizationId, Guid entryId) =>
        new() { OrganizationId = organizationId, CoalesceKey = $"assess.assess-alert:{organizationId}:{entryId}" };
}

internal sealed record MatchProject(Guid ProjectId) : IJob
{
    public static string Kind => "assess.match-project";

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

        organizations.UnionWith(await db.Database
            .SqlQuery<Guid>($"""SELECT organization_id AS "Value" FROM assess.organizations_with_alert({entry.Id})""")
            .ToListAsync(cancellationToken));

        foreach (var organization in organizations)
        {
            await jobs.EnqueueAsync(new AssessAlert(entry.Id), AssessAlert.Options(organization, entry.Id), cancellationToken);
        }
    }
}

internal sealed class AssessAlertHandler(IEntryQueries entries, AlertAssessor assessor, Remediations remediations, Estimating.EstimateTrigger estimates)
    : IJobHandler<AssessAlert>
{
    public async Task HandleAsync(AssessAlert job, JobContext context, CancellationToken cancellationToken)
    {
        if (await entries.FindAsync(job.EntryId, cancellationToken) is { } entry)
        {
            var touched = await assessor.AssessAsync(entry, AssessmentScope.Organization, cancellationToken);
            await remediations.SyncAsync(touched, actor: null, cancellationToken);
            await estimates.QueueAfterRulesAsync(entry.Id, cancellationToken);
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
    Notices notices,
    TimeProvider time)
    : IJobHandler<MatchProject>
{
    private const int Batch = 100;

    public async Task HandleAsync(MatchProject job, JobContext context, CancellationToken cancellationToken)
    {
        var project = await profiles.FindProjectAsync(job.ProjectId, cancellationToken)
            ?? new ProjectProfile(job.ProjectId, string.Empty, "normal", "internal", Archived: false, time.GetUtcNow(), []);

        var ids = new SortedSet<Guid>(await db.Findings.Where(f => f.ProjectId == job.ProjectId).Select(f => f.EntryId).Distinct().ToListAsync(cancellationToken));
        if (!project.Archived)
        {
            var keys = await KeysAsync(project, cancellationToken);
            ids.UnionWith(await entries.FindAffectingAsync(keys, time.GetUtcNow() - MatchProject.Lookback, cancellationToken));
        }

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

        if (notices.QuietlyRaised.Count > 0)
        {
            history.Add(EventKinds.NoticeBackfilled, new { notices = notices.QuietlyRaised.Count, important = notices.QuietlyRaisedImportant }, project: project.Id);
            await db.SaveChangesAsync(cancellationToken);
        }

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

internal sealed class EntryChangeSubscriber(IJobQueue jobs) : IEntryChangeSubscriber
{
    public Task OnEntryChangedAsync(EntryChanged change, CancellationToken cancellationToken) =>
        jobs.EnqueueAsync(new MatchEntry(change.EntryId), MatchEntry.Options(change.EntryId), cancellationToken);
}

internal sealed class ProfileChangeSubscriber(IJobQueue jobs) : IProfileChangeSubscriber
{
    public Task OnProfileChangedAsync(ProfileChanged change, CancellationToken cancellationToken) =>
        jobs.EnqueueAsync(new MatchProject(change.ProjectId), MatchProject.Options(change.OrganizationId, change.ProjectId), cancellationToken);
}
