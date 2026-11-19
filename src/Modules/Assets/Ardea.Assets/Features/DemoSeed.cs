using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;
using Ardea.Platform.Audit;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assets.Features;

/// <summary>
/// The three projects of the demo (<c>ardea seed-demo</c>, task 7.6): the example projects A, B and C of the requirements
/// under the names of the mockups, each with the versions installed before the spike week's security updates. They are
/// written as the profile endpoints write them, and each queues the project's reverse search.
/// </summary>
internal sealed class DemoSeed : IDemoSeedStep
{
    private sealed record Part(string? Key, string Name, string Kind, string Version, string Packaging = "upstream");

    private sealed record Demo(string Name, string Description, string Criticality, string Exposure, Part[] Parts);

    private static readonly Demo[] Projects =
    [
        new("Customer Portal", "Customer-facing portal: .NET, Angular, Umbraco and Azure SQL", "normal", "internet",
        [
            new("dotnet", ".NET", "runtime", "10.0.403"),
            new("angular", "Angular", "framework", "22.2.0"),
            new("umbraco", "Umbraco CMS", "cms", "17.2.0"),
            // A managed service: the catalogue does not know it, and records about self-hosted SQL Server do not concern it.
            new(null, "Azure SQL Database", "service", string.Empty, "unknown"),
        ]),
        new("Webshop", "Online shop on WordPress with PHP and MySQL", "normal", "internet",
        [
            new("wordpress", "WordPress", "cms", "6.9.1"),
            new("php", "PHP", "runtime", "8.4.26"),
            new("mysql", "MySQL Server", "database", "8.4.13"),
        ]),
        new("Platform Ops", "Business-critical platform: Kubernetes, Linux, Redis and PostgreSQL", "high", "internal",
        [
            new("kubernetes", "Kubernetes", "container", "1.34.2"),
            new("linux-kernel", "Linux kernel", "os", "6.12.58", "distribution"),
            new("redis", "Redis", "database", "8.2.3"),
            new("postgresql", "PostgreSQL", "database", "17.11"),
        ]),
    ];

    public int Order => DemoSeedOrder.Profile;

    public string Description => "three projects with their components";

    public async Task SeedAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        foreach (var demo in Projects)
        {
            await using var scope = context.CreateScope(context.OrganizationId, context.Members["nina"].UserId);
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<AssetsDbContext>();
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
            var components = demo.Parts.Select(p => new Component
            {
                Id = Guid.CreateVersion7(),
                ProjectId = project.Id,
                ProductKey = p.Key,
                CustomName = p.Key is null ? p.Name : null,
                Kind = p.Kind,
                Version = p.Version.Length == 0 ? null : p.Version,
                Packaging = p.Packaging,
                CreatedAt = now,
                ChangedAt = now,
            }).ToList();
            db.Components.AddRange(components);
            await db.SaveChangesAsync(cancellationToken);
            await ComponentKeys.RefreshAsync(db, services.GetRequiredService<IProductCatalog>(), components, cancellationToken);
            await services.GetRequiredService<IAuditLog>().RecordAsync(
                new AuditEntry("project.created", "project", project.Id.ToString(), new { project.Name, project.Criticality, project.Exposure }), cancellationToken);
            await services.GetRequiredService<IJobQueue>().EnqueueAsync(new ProjectProfileChanged(project.Id), ProjectProfileChanged.Options(project.Id), cancellationToken);
            await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        }
    }
}
