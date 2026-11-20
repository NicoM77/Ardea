using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ardea.Assessment.Persistence;

internal sealed class AssessDbContext(DbContextOptions<AssessDbContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public const string Schema = "assess";

    public DbSet<Alert> Alerts => Set<Alert>();

    public DbSet<Finding> Findings => Set<Finding>();

    public DbSet<Remediation> Remediations => Set<Remediation>();

    public DbSet<AssessEvent> Events => Set<AssessEvent>();

    public DbSet<OrgCounter> Counters => Set<OrgCounter>();

    public DbSet<MatchScope> MatchScopes => Set<MatchScope>();

    public DbSet<AlertAssessment> Assessments => Set<AlertAssessment>();

    public DbSet<AssessmentFeedback> Feedback => Set<AssessmentFeedback>();

    public DbSet<Notice> Notices => Set<Notice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Alert>(alert =>
        {
            alert.ToTable("alert", t => t.HasCheckConstraint("ck_alert_level", "level BETWEEN 1 AND 5"));
            alert.HasKey(a => a.Id);
            alert.Property(a => a.Id).HasDefaultValueSql("uuidv7()");
            alert.HasIndex(a => new { a.OrganizationId, a.EntryId }).IsUnique().HasDatabaseName("ux_alert_entry");
            alert.HasIndex(a => a.EntryId).HasDatabaseName("ix_alert_entry");
            alert.Property(a => a.EntryKey).HasMaxLength(300);
            alert.HasIndex(a => new { a.OrganizationId, a.Number }).IsUnique().HasDatabaseName("ux_alert_number");
            alert.HasIndex(a => new { a.OrganizationId, a.Level, a.Number }).HasDatabaseName("ix_alert_list");
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
                t.HasCheckConstraint("ck_finding_status", "status IN ('open', 'remediated', 'risk_accepted', 'not_affected')");
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
            finding.HasIndex(f => new { f.OrganizationId, f.Level, f.Id }).HasDatabaseName("ix_finding_org_level");
            finding.HasOne<Remediation>().WithMany().HasForeignKey(f => f.RemediationId).OnDelete(DeleteBehavior.SetNull);
            finding.HasIndex(f => new { f.AlertId, f.ComponentId }).IsUnique().HasDatabaseName("ux_finding_component");
            finding.HasIndex(f => new { f.ProjectId, f.EntryId }).HasDatabaseName("ix_finding_project_entry");
            finding.HasIndex(f => f.ComponentId).HasDatabaseName("ix_finding_component");
            finding.HasOne<Alert>().WithMany().HasForeignKey(f => f.AlertId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Remediation>(remediation =>
        {
            remediation.ToTable("remediation", t =>
            {
                t.HasCheckConstraint("ck_remediation_level", "level BETWEEN 1 AND 5");
                t.HasCheckConstraint("ck_remediation_status", "status IN ('open', 'closed')");
                t.HasCheckConstraint("ck_remediation_exploitation", "exploitation IN ('none', 'poc', 'active')");
            });
            remediation.HasKey(r => r.Id);
            remediation.Property(r => r.Id).HasDefaultValueSql("uuidv7()");
            remediation.Property(r => r.TargetVersion).HasMaxLength(100);
            remediation.Property(r => r.Exploitation).HasMaxLength(10);
            remediation.Property(r => r.Status).HasMaxLength(20);
            remediation.Property(r => r.CloseReason).HasMaxLength(40);
            remediation.HasIndex(r => new { r.OrganizationId, r.Number }).IsUnique().HasDatabaseName("ux_remediation_number");
            remediation.HasIndex(r => r.ComponentId).IsUnique().HasFilter("status <> 'closed'").HasDatabaseName("ux_remediation_component_open");
            remediation.HasIndex(r => new { r.OrganizationId, r.Status, r.Level, r.Number }).HasDatabaseName("ix_remediation_list");
            remediation.HasIndex(r => new { r.OrganizationId, r.Level, r.Number }).HasDatabaseName("ix_remediation_level");
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
            e.HasIndex(x => new { x.NoticeId, x.At }).HasDatabaseName("ix_assess_event_notice");
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

        modelBuilder.Entity<AlertAssessment>(assessment =>
        {
            assessment.ToTable("assessment", t =>
            {
                t.HasCheckConstraint("ck_assessment_engine", "engine IN ('rules', 'rules+model')");
                t.HasCheckConstraint("ck_assessment_counts", "attempts BETWEEN 1 AND 2 AND tokens_in >= 0 AND tokens_out >= 0 AND duration_ms >= 0");
            });
            assessment.HasKey(a => a.Id);
            assessment.Property(a => a.Id).HasDefaultValueSql("uuidv7()");
            assessment.Property(a => a.InputFingerprint).HasMaxLength(64);
            assessment.Property(a => a.Engine).HasMaxLength(20);
            assessment.Property(a => a.Provider).HasMaxLength(40);
            assessment.Property(a => a.Model).HasMaxLength(200);
            assessment.Property(a => a.PromptVersion).HasMaxLength(20);
            assessment.Property(a => a.Language).HasMaxLength(20);
            assessment.Property(a => a.Output).HasColumnType("jsonb");
            assessment.HasIndex(a => a.AlertId).HasDatabaseName("ix_assessment_alert");
            assessment.HasIndex(a => new { a.OrganizationId, a.CreatedAt }).HasDatabaseName("ix_assessment_org_created");
            assessment.HasOne<Alert>().WithMany().HasForeignKey(a => a.AlertId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssessmentFeedback>(feedback =>
        {
            feedback.ToTable("assessment_feedback", t =>
            {
                t.HasCheckConstraint("ck_assessment_feedback_kind", "kind IN ('confirmed', 'corrected')");
                t.HasCheckConstraint("ck_assessment_feedback_source", "verdict_source IN ('rules', 'model', 'human')");
                t.HasCheckConstraint("ck_assessment_feedback_verdicts", "verdict_before IN ('affected', 'likely', 'uncertain', 'unlikely', 'not_affected') AND (verdict IS NULL OR verdict IN ('affected', 'likely', 'uncertain', 'unlikely', 'not_affected'))");
                t.HasCheckConstraint("ck_assessment_feedback_levels", "level_before BETWEEN 1 AND 5 AND (expected_level IS NULL OR expected_level BETWEEN 1 AND 5)");
                t.HasCheckConstraint("ck_assessment_feedback_correction", "kind = 'confirmed' OR ((verdict IS NOT NULL OR expected_level IS NOT NULL) AND reason IS NOT NULL)");
            });
            feedback.HasKey(f => f.Id);
            feedback.Property(f => f.Id).HasDefaultValueSql("uuidv7()");
            feedback.Property(f => f.Kind).HasMaxLength(20);
            feedback.Property(f => f.VerdictSource).HasMaxLength(10);
            feedback.Property(f => f.VerdictBefore).HasMaxLength(20);
            feedback.Property(f => f.Verdict).HasMaxLength(20);
            feedback.Property(f => f.Reason).HasMaxLength(2000);
            feedback.HasIndex(f => new { f.FindingId, f.CreatedAt }).HasDatabaseName("ix_assessment_feedback_finding");
            feedback.HasIndex(f => f.AssessmentId).HasDatabaseName("ix_assessment_feedback_assessment");
            feedback.HasOne<Finding>().WithMany().HasForeignKey(f => f.FindingId).OnDelete(DeleteBehavior.Cascade);
            feedback.HasOne<AlertAssessment>().WithMany().HasForeignKey(f => f.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Notice>(notice =>
        {
            notice.ToTable("notice", t =>
            {
                t.HasCheckConstraint("ck_notice_state", "state IN ('open', 'affects_us', 'not_affected', 'withdrawn')");
                t.HasCheckConstraint("ck_notice_decided", "state IN ('open', 'withdrawn') OR (decided_by IS NOT NULL AND decided_at IS NOT NULL)");
                t.HasCheckConstraint("ck_notice_exploitation", "exploitation IN ('none', 'poc', 'active') AND (decided_exploitation IS NULL OR decided_exploitation IN ('none', 'poc', 'active'))");
                t.HasCheckConstraint("ck_notice_reappeared", "reappeared_reason IS NULL OR reappeared_reason IN ('exploited', 'kev')");
            });
            notice.HasKey(n => n.Id);
            notice.Property(n => n.Id).HasDefaultValueSql("uuidv7()");
            notice.Property(n => n.EntryKey).HasMaxLength(300);
            notice.Property(n => n.State).HasMaxLength(20);
            notice.Property(n => n.ReappearedReason).HasMaxLength(20);
            notice.Property(n => n.DecidedExploitation).HasMaxLength(10);
            notice.Property(n => n.Exploitation).HasMaxLength(10).HasDefaultValue("none");
            notice.Property(n => n.Exploited).HasComputedColumnSql("exploitation = 'active'", stored: true);
            notice.Property(n => n.ScoreRank).HasComputedColumnSql("coalesce(cvss_score, -1)", stored: true);
            notice.HasIndex(n => new { n.OrganizationId, n.EntryId }).IsUnique().HasDatabaseName("ux_notice_entry");
            notice.HasIndex(n => new { n.OrganizationId, n.State, n.IsBackfill, n.Exploited, n.ScoreRank, n.RaisedAt, n.Id })
                .IsDescending(false, false, false, true, true, true, true).HasDatabaseName("ix_notice_list");
            notice.HasIndex(n => n.RemediationId).HasDatabaseName("ix_notice_remediation");
            notice.HasOne<Remediation>().WithMany().HasForeignKey(n => n.RemediationId).OnDelete(DeleteBehavior.SetNull);
        });

        ApplyTenantFilters(modelBuilder);
    }
}

internal sealed class AssessDesignTimeFactory : IDesignTimeDbContextFactory<AssessDbContext>
{
    public AssessDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AssessDbContext>().UseArdeaNpgsql("Host=localhost;Database=ardea", AssessDbContext.Schema).Options, new TenantContext());
}
