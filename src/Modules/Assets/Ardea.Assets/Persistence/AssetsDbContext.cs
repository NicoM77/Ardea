using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ardea.Assets.Persistence;

/// <summary>Schema <c>assets</c>: organisation data (master plan 4.1, 4.5).</summary>
internal sealed class AssetsDbContext(DbContextOptions<AssetsDbContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public const string Schema = "assets";

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Component> Components => Set<Component>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Project>(project =>
        {
            project.ToTable("project", t =>
            {
                t.HasCheckConstraint("ck_project_criticality", "criticality IN ('low', 'normal', 'high')");
                t.HasCheckConstraint("ck_project_exposure", "exposure IN ('internet', 'internal', 'isolated')");
            });
            project.HasKey(p => p.Id);
            project.Property(p => p.Id).HasDefaultValueSql("uuidv7()");
            project.Property(p => p.Name).HasMaxLength(200);
            project.Property(p => p.Description).HasMaxLength(2000);
            project.Property(p => p.Criticality).HasMaxLength(10);
            project.Property(p => p.Exposure).HasMaxLength(10);
            project.HasIndex(p => new { p.OrganizationId, p.Name }).HasDatabaseName("ix_project_org_name");
        });

        modelBuilder.Entity<Component>(component =>
        {
            component.ToTable("component", t =>
            {
                t.HasCheckConstraint("ck_component_product", "product_key IS NOT NULL OR custom_name IS NOT NULL");
                t.HasCheckConstraint("ck_component_kind",
                    "kind IN ('os', 'runtime', 'framework', 'cms', 'database', 'messaging', 'container', 'network', 'virtualization', 'library', 'plugin', 'service', 'other')");
                t.HasCheckConstraint("ck_component_packaging", "packaging IN ('upstream', 'distribution', 'unknown')");
                t.HasCheckConstraint("ck_component_exposure", "exposure IS NULL OR exposure IN ('internet', 'internal', 'isolated')");
                t.HasCheckConstraint("ck_component_criticality", "criticality IS NULL OR criticality IN ('low', 'normal', 'high')");
                t.HasCheckConstraint("ck_component_protection", "protection IN ('none', 'effective')");
                t.HasCheckConstraint("ck_component_origin", "origin IN ('manual', 'repository')");
                t.HasCheckConstraint("ck_component_scope", "dependency_scope IS NULL OR dependency_scope IN ('runtime', 'development', 'unknown')");
                t.HasCheckConstraint("ck_component_relation", "dependency_relation IS NULL OR dependency_relation IN ('direct', 'transitive', 'unknown')");
            });
            component.HasKey(c => c.Id);
            component.Property(c => c.Id).HasDefaultValueSql("uuidv7()");
            component.Property(c => c.ProductKey).HasMaxLength(200);
            component.Property(c => c.CustomVendor).HasMaxLength(200);
            component.Property(c => c.CustomName).HasMaxLength(200);
            component.Property(c => c.Kind).HasMaxLength(20);
            component.Property(c => c.Version).HasMaxLength(100);
            component.Property(c => c.VersionScheme).HasMaxLength(30);
            component.Property(c => c.Packaging).HasMaxLength(20);
            component.Property(c => c.Exposure).HasMaxLength(10);
            component.Property(c => c.Criticality).HasMaxLength(10);
            component.Property(c => c.Protection).HasMaxLength(10);
            component.Property(c => c.ProtectionNote).HasMaxLength(1000);
            component.Property(c => c.Origin).HasMaxLength(20);
            component.Property(c => c.Purl).HasMaxLength(500);
            component.Property(c => c.DependencyScope).HasMaxLength(20);
            component.Property(c => c.DependencyRelation).HasMaxLength(20);
            component.Property(c => c.Evidence).HasColumnType("jsonb");
            component.HasIndex(c => c.ProjectId).HasDatabaseName("ix_component_project");
            component.HasIndex(c => new { c.OrganizationId, c.ProductKey }).HasDatabaseName("ix_component_org_product");
            component.HasOne<Project>().WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Cascade);
            component.HasOne<Component>().WithMany().HasForeignKey(c => c.ParentComponentId).OnDelete(DeleteBehavior.SetNull);
        });

        ApplyTenantFilters(modelBuilder);
    }
}

internal sealed class AssetsDesignTimeFactory : IDesignTimeDbContextFactory<AssetsDbContext>
{
    public AssetsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AssetsDbContext>().UseArdeaNpgsql("Host=localhost;Database=ardea", AssetsDbContext.Schema).Options, new TenantContext());
}
