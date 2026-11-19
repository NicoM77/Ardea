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
            finding.Property(f => f.Status).HasMaxLength(20);
            finding.Property(f => f.StatusReason).HasMaxLength(40);
            finding.Property(f => f.StatusNote).HasMaxLength(2000);
            finding.HasIndex(f => new { f.AlertId, f.ComponentId }).IsUnique().HasDatabaseName("ux_finding_component");
            // The reverse search re-evaluates what a project already has (master plan 7.7).
            finding.HasIndex(f => new { f.ProjectId, f.EntryId }).HasDatabaseName("ix_finding_project_entry");
            finding.HasIndex(f => f.ComponentId).HasDatabaseName("ix_finding_component");
            finding.HasOne<Alert>().WithMany().HasForeignKey(f => f.AlertId).OnDelete(DeleteBehavior.Cascade);
        });

        ApplyTenantFilters(modelBuilder);
    }
}

internal sealed class AssessDesignTimeFactory : IDesignTimeDbContextFactory<AssessDbContext>
{
    public AssessDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AssessDbContext>().UseArdeaNpgsql("Host=localhost;Database=ardea", AssessDbContext.Schema).Options, new TenantContext());
}
