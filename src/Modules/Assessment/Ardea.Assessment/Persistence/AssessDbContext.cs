using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ardea.Assessment.Persistence;

/// <summary>Schema <c>assess</c>: organisation data (master plan 4.1, 4.6).</summary>
internal sealed class AssessDbContext(DbContextOptions<AssessDbContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public const string Schema = "assess";

    public DbSet<Alert> Alerts => Set<Alert>();

    public DbSet<Finding> Findings => Set<Finding>();

    public DbSet<Remediation> Remediations => Set<Remediation>();

    public DbSet<AssessEvent> Events => Set<AssessEvent>();

    public DbSet<OrgCounter> Counters => Set<OrgCounter>();

    public DbSet<MatchScope> MatchScopes => Set<MatchScope>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Alert>(alert =>
        {
            alert.ToTable("alert", t => t.HasCheckConstraint("ck_alert_level", "level BETWEEN 1 AND 5"));
            alert.HasKey(a => a.Id);
            alert.Property(a => a.Id).HasDefaultValueSql("uuidv7()");
            alert.HasIndex(a => new { a.OrganizationId, a.EntryId }).IsUnique().HasDatabaseName("ux_alert_entry");
            // The system operation «reassessment» asks across organisations: who has an alert for this entry (master plan 9.2).
            alert.HasIndex(a => a.EntryId).HasDatabaseName("ix_alert_entry");
            alert.Property(a => a.EntryKey).HasMaxLength(300);
            alert.HasIndex(a => new { a.OrganizationId, a.Number }).IsUnique().HasDatabaseName("ux_alert_number");
        });

        modelBuilder.Entity<Finding>(finding =>
        {
            finding.ToTable("finding", t =>
            {
                t.HasCheckConstraint("ck_finding_level", "level BETWEEN 1 AND 5");
                t.HasCheckConstraint("ck_finding_match_type", "match_type IN ('package', 'cpe', 'product', 'variant', 'ecosystem', 'name')");
                t.HasCheckConstraint("ck_finding_version_verdict", "version_verdict IS NULL OR version_verdict IN ('in_range', 'out_of_range', 'unknown')");
                t.HasCheckConstraint("ck_finding_verdict", "verdict IN ('affected', 'likely', 'uncertain', 'unlikely', 'not_affected')");
                t.HasCheckConstraint("ck_finding_verdict_source", "verdict_source IN ('rules', 'model', 'human')");
                t.HasCheckConstraint("ck_finding_status", "status IN ('open', 'in_review', 'remediated', 'risk_accepted', 'not_affected')");
            });
            finding.HasKey(f => f.Id);
            finding.Property(f => f.Id).HasDefaultValueSql("uuidv7()");
            finding.Property(f => f.MatchType).HasMaxLength(20);
            finding.Property(f => f.VersionVerdict).HasMaxLength(20);
            finding.Property(f => f.VersionReason).HasMaxLength(40);
            finding.Property(f => f.FixedVersion).HasMaxLength(100);
            finding.Property(f => f.Verdict).HasMaxLength(20);
            finding.Property(f => f.VerdictSource).HasMaxLength(10);
            finding.Property(f => f.Evidence).HasColumnType("jsonb");
            finding.Property(f => f.Factors).HasColumnType("jsonb");
            finding.Property(f => f.Status).HasMaxLength(20);
            finding.Property(f => f.StatusReason).HasMaxLength(40);
            finding.Property(f => f.StatusNote).HasMaxLength(2000);
            finding.Property(f => f.Exploitation).HasMaxLength(10);
            finding.Property(f => f.ClosedVersionVerdict).HasMaxLength(20);
            finding.HasIndex(f => f.RemediationId).HasDatabaseName("ix_finding_remediation");
            finding.HasIndex(f => new { f.OrganizationId, f.Level }).HasDatabaseName("ix_finding_org_level");
            finding.HasOne<Remediation>().WithMany().HasForeignKey(f => f.RemediationId).OnDelete(DeleteBehavior.SetNull);
            finding.HasIndex(f => new { f.AlertId, f.ComponentId }).IsUnique().HasDatabaseName("ux_finding_component");
            // The reverse search re-evaluates what a project already has (master plan 7.7).
            finding.HasIndex(f => new { f.ProjectId, f.EntryId }).HasDatabaseName("ix_finding_project_entry");
            finding.HasIndex(f => f.ComponentId).HasDatabaseName("ix_finding_component");
            finding.HasOne<Alert>().WithMany().HasForeignKey(f => f.AlertId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Remediation>(remediation =>
        {
            remediation.ToTable("remediation", t =>
            {
                t.HasCheckConstraint("ck_remediation_level", "level BETWEEN 1 AND 5");
                t.HasCheckConstraint("ck_remediation_status", "status IN ('open', 'in_review', 'closed')");
                t.HasCheckConstraint("ck_remediation_exploitation", "exploitation IN ('none', 'poc', 'active')");
            });
            remediation.HasKey(r => r.Id);
            remediation.Property(r => r.Id).HasDefaultValueSql("uuidv7()");
            remediation.Property(r => r.TargetVersion).HasMaxLength(100);
            remediation.Property(r => r.Exploitation).HasMaxLength(10);
            remediation.Property(r => r.Status).HasMaxLength(20);
            remediation.Property(r => r.CloseReason).HasMaxLength(40);
            remediation.HasIndex(r => new { r.OrganizationId, r.Number }).IsUnique().HasDatabaseName("ux_remediation_number");
            // At most one action per component is not closed (master plan 4.6).
            remediation.HasIndex(r => r.ComponentId).IsUnique().HasFilter("status <> 'closed'").HasDatabaseName("ux_remediation_component_open");
            // The list: by status, level and number within the organisation (master plan 10.2).
            remediation.HasIndex(r => new { r.OrganizationId, r.Status, r.Level, r.Number }).HasDatabaseName("ix_remediation_list");
        });

        modelBuilder.Entity<AssessEvent>(e =>
        {
            e.ToTable("assess_event");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("uuidv7()");
            e.Property(x => x.Kind).HasMaxLength(40);
            e.Property(x => x.Data).HasColumnType("jsonb");
            e.HasIndex(x => new { x.RemediationId, x.At }).HasDatabaseName("ix_assess_event_remediation");
            e.HasIndex(x => x.FindingId).HasDatabaseName("ix_assess_event_finding");
            e.HasIndex(x => new { x.OrganizationId, x.At }).HasDatabaseName("ix_assess_event_org_at");
        });

        modelBuilder.Entity<OrgCounter>(counter =>
        {
            counter.ToTable("org_counter");
            counter.HasKey(c => c.OrganizationId);
        });

        modelBuilder.Entity<MatchScope>(scope =>
        {
            scope.ToTable("match_scope");
            scope.HasKey(s => s.Id);
            scope.Property(s => s.Id).HasDefaultValueSql("uuidv7()");
            scope.HasIndex(s => new { s.ProjectId, s.RepositoryLinkId }).IsUnique().AreNullsDistinct(false).HasDatabaseName("ux_match_scope");
        });

        ApplyTenantFilters(modelBuilder);
    }
}

internal sealed class AssessDesignTimeFactory : IDesignTimeDbContextFactory<AssessDbContext>
{
    public AssessDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AssessDbContext>().UseArdeaNpgsql("Host=localhost;Database=ardea", AssessDbContext.Schema).Options, new TenantContext());
}
