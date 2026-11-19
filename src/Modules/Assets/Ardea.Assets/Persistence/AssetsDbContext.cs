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

    public DbSet<ComponentKey> ComponentKeys => Set<ComponentKey>();

    public DbSet<RepositoryConnection> RepositoryConnections => Set<RepositoryConnection>();

    public DbSet<RepositoryLink> RepositoryLinks => Set<RepositoryLink>();

    public DbSet<ComponentSuggestion> ComponentSuggestions => Set<ComponentSuggestion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Project>(project =>
        {
            project.ToTable("project", t =>
            {
                t.HasCheckConstraint("ck_project_criticality", "criticality IN ('low', 'normal', 'high')");
                t.HasCheckConstraint("ck_project_exposure", "exposure IN ('internet', 'internal', 'isolated')");
                t.HasCheckConstraint("ck_project_kind", "kind IN ('project', 'organization_stack')");
            });
            project.HasKey(p => p.Id);
            project.Property(p => p.Id).HasDefaultValueSql("uuidv7()");
            project.Property(p => p.Kind).HasMaxLength(20).HasDefaultValue(Project.ProjectKind);
            project.Property(p => p.Name).HasMaxLength(200);
            project.Property(p => p.Description).HasMaxLength(2000);
            project.Property(p => p.Criticality).HasMaxLength(10);
            project.Property(p => p.Exposure).HasMaxLength(10);
            project.HasIndex(p => new { p.OrganizationId, p.Name }).HasDatabaseName("ix_project_org_name");
            // Exactly one stack per organisation (master plan 17.2): created with the organisation, never a second.
            project.HasIndex(p => p.OrganizationId).IsUnique().HasFilter("kind = 'organization_stack'").HasDatabaseName("ux_project_stack");
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
                // «none» exactly when there is no version (ADR 046).
                t.HasCheckConstraint("ck_component_version_precision",
                    "version_precision IN ('none', 'line', 'exact') AND (version_precision = 'none') = (version IS NULL OR btrim(version) = '')");
                t.HasCheckConstraint("ck_component_notice_threshold", "notice_threshold IN ('only_important', 'everything')");
            });
            component.HasKey(c => c.Id);
            component.Property(c => c.Id).HasDefaultValueSql("uuidv7()");
            component.Property(c => c.ProductKey).HasMaxLength(200);
            component.Property(c => c.CustomVendor).HasMaxLength(200);
            component.Property(c => c.CustomName).HasMaxLength(200);
            component.Property(c => c.Kind).HasMaxLength(20);
            component.Property(c => c.Version).HasMaxLength(100);
            component.Property(c => c.VersionPrecision).HasMaxLength(10).HasDefaultValue(VersionPrecisions.None);
            component.Property(c => c.NoticeThreshold).HasMaxLength(20).HasDefaultValue(NoticeThresholds.OnlyImportant);
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

        modelBuilder.Entity<ComponentKey>(key =>
        {
            key.ToTable("component_key", t =>
            {
                t.HasCheckConstraint("ck_component_key_type", "key_type IN ('cpe', 'product', 'purl')");
                t.HasCheckConstraint("ck_component_key_strength", "strength IN ('strong', 'weak')");
            });
            key.HasKey(k => k.Id);
            key.Property(k => k.Id).HasDefaultValueSql("uuidv7()");
            key.Property(k => k.KeyType).HasMaxLength(10);
            key.Property(k => k.KeyValue).HasMaxLength(500);
            key.Property(k => k.Strength).HasMaxLength(10);
            key.HasIndex(k => new { k.ComponentId, k.KeyType, k.KeyValue }).IsUnique().HasDatabaseName("ux_component_key");
            // The forward search asks by key across projects: entry → components (master plan 7.2).
            key.HasIndex(k => new { k.KeyType, k.KeyValue }).HasDatabaseName("ix_component_key_value");
            key.HasOne<Component>().WithMany().HasForeignKey(k => k.ComponentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RepositoryConnection>(connection =>
        {
            connection.ToTable("repository_connection", t =>
                t.HasCheckConstraint("ck_repository_connection_kind", "token_kind IN ('none', 'fine_grained', 'classic', 'other')"));
            connection.HasKey(c => c.Id);
            connection.Property(c => c.Id).HasDefaultValueSql("uuidv7()");
            connection.Property(c => c.Name).HasMaxLength(100);
            connection.Property(c => c.HostId).HasMaxLength(50);
            connection.Property(c => c.TokenHint).HasMaxLength(4);
            connection.Property(c => c.TokenKind).HasMaxLength(20);
            connection.Property(c => c.Login).HasMaxLength(100);
            connection.Property(c => c.LastError).HasMaxLength(500);
            connection.HasIndex(c => c.OrganizationId).HasDatabaseName("ix_repository_connection_org");
        });

        modelBuilder.Entity<RepositoryLink>(link =>
        {
            link.ToTable("repository_link", t => t.HasCheckConstraint("ck_repository_link_state", "state IN ('pending', 'ok', 'partial', 'failing')"));
            link.HasKey(l => l.Id);
            link.Property(l => l.Id).HasDefaultValueSql("uuidv7()");
            link.Property(l => l.Owner).HasMaxLength(100);
            link.Property(l => l.Name).HasMaxLength(100);
            link.Property(l => l.Ref).HasMaxLength(250);
            link.Property(l => l.Path).HasMaxLength(500);
            link.Property(l => l.State).HasMaxLength(10);
            link.Property(l => l.StateReason).HasMaxLength(500);
            link.Property(l => l.LastCommitSha).HasMaxLength(64);
            link.Property(l => l.PendingShrinkCommit).HasMaxLength(64);
            link.Property(l => l.HeadEtag).HasMaxLength(200);
            link.HasIndex(l => new { l.ProjectId, l.ConnectionId, l.Owner, l.Name, l.Path }).IsUnique().AreNullsDistinct(false).HasDatabaseName("ux_repository_link");
            // The schedule's question across organisations (master plan 9.2): which links are due.
            link.HasIndex(l => l.NextCheckAt).HasDatabaseName("ix_repository_link_due");
            link.HasOne<Project>().WithMany().HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Cascade);
            link.HasOne<RepositoryConnection>().WithMany().HasForeignKey(l => l.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ComponentSuggestion>(suggestion =>
        {
            suggestion.ToTable("component_suggestion", t => t.HasCheckConstraint("ck_component_suggestion_state", "state IN ('open', 'accepted', 'dismissed')"));
            suggestion.HasKey(s => s.Id);
            suggestion.Property(s => s.Id).HasDefaultValueSql("uuidv7()");
            suggestion.Property(s => s.ProductKey).HasMaxLength(200);
            suggestion.Property(s => s.Version).HasMaxLength(100);
            suggestion.Property(s => s.Evidence).HasColumnType("jsonb");
            suggestion.Property(s => s.Fingerprint).HasMaxLength(64);
            suggestion.Property(s => s.State).HasMaxLength(10);
            suggestion.HasIndex(s => new { s.RepositoryLinkId, s.Fingerprint }).IsUnique().HasDatabaseName("ux_component_suggestion");
            suggestion.HasIndex(s => new { s.ProjectId, s.State }).HasDatabaseName("ix_component_suggestion_project");
            suggestion.HasOne<RepositoryLink>().WithMany().HasForeignKey(s => s.RepositoryLinkId).OnDelete(DeleteBehavior.Cascade);
            suggestion.HasOne<Component>().WithMany().HasForeignKey(s => s.ComponentId).OnDelete(DeleteBehavior.SetNull);
        });

        // A repository component belongs to its link: removing the link removes it (master plan 10.2).
        modelBuilder.Entity<Component>().HasOne<RepositoryLink>().WithMany().HasForeignKey(c => c.RepositoryLinkId).OnDelete(DeleteBehavior.Cascade);

        ApplyTenantFilters(modelBuilder);
    }
}

internal sealed class AssetsDesignTimeFactory : IDesignTimeDbContextFactory<AssetsDbContext>
{
    public AssetsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AssetsDbContext>().UseArdeaNpgsql("Host=localhost;Database=ardea", AssetsDbContext.Schema).Options, new TenantContext());
}
