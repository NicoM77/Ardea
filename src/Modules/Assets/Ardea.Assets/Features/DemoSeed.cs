using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;
using Ardea.Platform.Audit;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assets.Features;

internal sealed class DemoSeed : IDemoSeedStep
{
    private sealed record Part(string? Key, string Version, string Packaging = "upstream", string? Name = null, string? Vendor = null, string? Kind = null);

    private sealed record Demo(string Name, string Description, string Criticality, string Exposure, Part[] Parts);

    private sealed record StackEntry(string Key, string? Version = null, string? Precision = null, string? Threshold = null);

    private static readonly StackEntry[] Stack =
    [
        new("ubuntu", "24.04", VersionPrecisions.Line),
        new("nginx"),
        new("docker-engine"),
        new("kubernetes"),
        new("nodejs", "22"),
        new("angular"),
        new("redis"),
        new("fortios", Threshold: NoticeThresholds.Everything),
        new("php"),
        new("mysql"),
        new("postgresql", "17", VersionPrecisions.Line, NoticeThresholds.Everything),
        new("dotnet"),
    ];

    private static readonly Demo[] Projects =
    [
        new("Customer Portal", "Customer-facing portal: .NET, Angular, Umbraco and Azure SQL", "normal", "internet",
        [
            new("dotnet", DemoUpdate.DotnetBefore),
            new("angular", "22.2.0"),
            new("umbraco", "17.2.0"),
            new(null, string.Empty, "unknown", "Azure SQL Database", "Microsoft", "service"),
        ]),
        new("Webshop", "Online shop on WordPress with PHP and MySQL", "normal", "internet",
        [
            new("wordpress", "6.9.1"),
            new("php", "8.4.26"),
            new("mysql", "8.4.13"),
        ]),
        new("Platform Ops", "Business-critical platform: Kubernetes, Linux, Redis and PostgreSQL", "high", "internal",
        [
            new("kubernetes", "1.34.2"),
            new("linux-kernel", "6.12.58", "distribution"),
            new("redis", "8.2.3"),
            new("postgresql", "17.11"),
        ]),
    ];

    public int Order => DemoSeedOrder.Profile;

    public string Description => "three projects with their components, and the stack";

    public async Task SeedAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        foreach (var demo in Projects)
        {
            await using var scope = context.CreateScope(context.OrganizationId, context.Members[DemoUpdate.Owner].UserId);
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<AssetsDbContext>();
            var catalog = services.GetRequiredService<IProductCatalog>();
            var now = context.Time.GetUtcNow();
            var project = new Project
            {
                Id = Guid.CreateVersion7(),
                Name = demo.Name,
                Description = demo.Description,
                Criticality = demo.Criticality,
                Exposure = demo.Exposure,
                CreatedAt = now,
                ChangedAt = now,
            };
            db.Projects.Add(project);
            var components = new List<Component>(demo.Parts.Length);
            foreach (var part in demo.Parts)
            {
                var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
                var input = new ComponentInput(null, null, part.Key, part.Vendor, part.Name, part.Kind, part.Version, part.Packaging, null, null, null, null, null, null);
                var values = await ComponentRules.CheckAsync(input, catalog, errors, string.Empty, cancellationToken)
                    ?? throw new InvalidOperationException($"The demo component {part.Key ?? part.Name} is not valid: {string.Join(" ", errors.SelectMany(e => e.Value))}");
                var component = new Component { Id = Guid.CreateVersion7(), ProjectId = project.Id, Kind = values.Kind, CreatedAt = now, ChangedAt = now };
                values.ApplyTo(component);
                components.Add(component);
            }

            db.Components.AddRange(components);
            await db.SaveChangesAsync(cancellationToken);
            await ComponentKeys.RefreshAsync(db, catalog, components, cancellationToken);
            await services.GetRequiredService<IAuditLog>().RecordAsync(
                new AuditEntry("project.created", "project", project.Id.ToString(), new { project.Name, project.Criticality, project.Exposure }), cancellationToken);
            await services.GetRequiredService<IJobQueue>().EnqueueAsync(new ProjectProfileChanged(project.Id), ProjectProfileChanged.Options(project.Id), cancellationToken);
            await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        }

        await SeedStackAsync(context, cancellationToken);
    }

    private static async Task SeedStackAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        await using var scope = context.CreateScope(context.OrganizationId, context.Members[DemoUpdate.Owner].UserId);
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AssetsDbContext>();
        var catalog = services.GetRequiredService<IProductCatalog>();
        var now = context.Time.GetUtcNow();
        var stackId = await db.Projects.Where(p => p.Kind == Project.StackKind).Select(p => p.Id).SingleAsync(cancellationToken);
        var components = new List<Component>(Stack.Length);
        foreach (var entry in Stack)
        {
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var input = new ComponentInput(null, null, entry.Key, null, null, null, entry.Version, null, null, null, null, null, null, null, entry.Precision, entry.Threshold);
            var values = await ComponentRules.CheckAsync(input, catalog, errors, string.Empty, cancellationToken)
                ?? throw new InvalidOperationException($"The demo stack entry {entry.Key} is not valid: {string.Join(" ", errors.SelectMany(e => e.Value))}");
            var component = new Component { Id = Guid.CreateVersion7(), ProjectId = stackId, Kind = values.Kind, CreatedAt = now, ChangedAt = now };
            values.ApplyTo(component);
            components.Add(component);
        }

        db.Components.AddRange(components);
        await db.SaveChangesAsync(cancellationToken);
        await ComponentKeys.RefreshAsync(db, catalog, components, cancellationToken);
        await services.GetRequiredService<IAuditLog>().RecordAsync(
            new AuditEntry("profile.replaced", "project", stackId.ToString(), new { created = components.Select(c => c.Id) }), cancellationToken);
        await services.GetRequiredService<IJobQueue>().EnqueueAsync(new ProjectProfileChanged(stackId), ProjectProfileChanged.Options(stackId), cancellationToken);
        await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
    }
}

internal sealed class DemoUpdate : IDemoSeedStep
{
    public const string Owner = "nina";
    public const string DotnetBefore = "10.0.403";
    public const string DotnetAfter = "10.0.404";

    public int Order => DemoSeedOrder.Updates;

    public string Description => "a security update in a profile";

    public async Task SeedAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        await using var scope = context.CreateScope(context.OrganizationId, context.Members[Owner].UserId);
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AssetsDbContext>();
        var catalog = services.GetRequiredService<IProductCatalog>();
        var component = await db.Components.SingleAsync(c => c.ProductKey == "dotnet" && c.Version == DotnetBefore, cancellationToken);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var input = new ComponentInput(null, null, component.ProductKey, null, null, component.Kind, DotnetAfter, component.Packaging, null, null,
            component.Exposure, component.Criticality, component.Protection, component.ProtectionNote);
        var values = await ComponentRules.CheckAsync(input, catalog, errors, string.Empty, cancellationToken)
            ?? throw new InvalidOperationException($"The demo update is not valid: {string.Join(" ", errors.SelectMany(e => e.Value))}");
        values.ApplyTo(component);
        component.ChangedAt = context.Time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await ComponentKeys.RefreshAsync(db, catalog, [component], cancellationToken);
        await services.GetRequiredService<IAuditLog>().RecordAsync(new AuditEntry("component.changed", "component", component.Id.ToString(), values), cancellationToken);
        await services.GetRequiredService<IJobQueue>().EnqueueAsync(
            new ProjectProfileChanged(component.ProjectId), ProjectProfileChanged.Options(component.ProjectId), cancellationToken);
        await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        context.Report($".NET {DotnetBefore} updated to {DotnetAfter} in the Customer Portal");
    }
}
