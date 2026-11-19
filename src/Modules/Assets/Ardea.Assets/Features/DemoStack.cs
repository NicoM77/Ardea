using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;
using Ardea.Platform.Audit;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assets.Features;

/// <summary>
/// Stack entries the demo's records hit (<c>ardea seed-demo</c>, task 5a.3): technologies the organisation runs besides the
/// projects, without versions, one with a line and the threshold «everything». Added before the records arrive, they give
/// the Notices page new, answered and exploited notices (ADR 047). Written like the component endpoint writes them, with
/// the stack's reverse search queued.
/// </summary>
internal sealed class DemoStack : IDemoSeedStep
{
    private static readonly (string Key, string? Line, string Threshold)[] Entries =
    [
        ("linux-kernel", null, NoticeThresholds.OnlyImportant),
        ("php", null, NoticeThresholds.OnlyImportant),
        ("mysql", null, NoticeThresholds.OnlyImportant),
        ("postgresql", "17", NoticeThresholds.Everything),
        ("dotnet", null, NoticeThresholds.OnlyImportant),
    ];

    public int Order => DemoSeedOrder.Profile;

    public string Description => "stack entries the records raise notices for";

    public async Task SeedAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        await using var scope = context.CreateScope(context.OrganizationId, context.Members[DemoUpdate.Owner].UserId);
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AssetsDbContext>();
        var catalog = services.GetRequiredService<IProductCatalog>();
        var stack = await db.Projects.SingleAsync(p => p.Kind == Project.StackKind, cancellationToken);
        var now = context.Time.GetUtcNow();
        var components = new List<Component>(Entries.Length);
        foreach (var (key, line, threshold) in Entries)
        {
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var input = new ComponentInput(
                null, null, key, null, null, null, line, null, null, null, null, null, null, null, line is null ? null : VersionPrecisions.Line, threshold);
            var values = await ComponentRules.CheckAsync(input, catalog, errors, string.Empty, cancellationToken)
                ?? throw new InvalidOperationException($"The demo stack entry {key} is not valid: {string.Join(" ", errors.SelectMany(e => e.Value))}");
            var component = new Component { Id = Guid.CreateVersion7(), ProjectId = stack.Id, Kind = values.Kind, CreatedAt = now, ChangedAt = now };
            values.ApplyTo(component);
            components.Add(component);
            await services.GetRequiredService<IAuditLog>().RecordAsync(new AuditEntry("component.added", "component", component.Id.ToString(), values), cancellationToken);
        }

        db.Components.AddRange(components);
        await db.SaveChangesAsync(cancellationToken);
        await ComponentKeys.RefreshAsync(db, catalog, components, cancellationToken);
        await services.GetRequiredService<IJobQueue>().EnqueueAsync(new ProjectProfileChanged(stack.Id), ProjectProfileChanged.Options(stack.Id), cancellationToken);
        await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        context.Report($"{components.Count} entries in the stack");
    }
}
