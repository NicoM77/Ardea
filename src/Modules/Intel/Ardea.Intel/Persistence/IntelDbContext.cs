using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ardea.Intel.Persistence;

/// <summary>Schema <c>intel</c>: global data, the same for every organisation (master plan 4.1, 4.4).</summary>
internal sealed class IntelDbContext(DbContextOptions<IntelDbContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public const string Schema = "intel";

    public DbSet<SourceRow> Sources => Set<SourceRow>();

    public DbSet<SourceRunRow> SourceRuns => Set<SourceRunRow>();

    public DbSet<SourceRecordRow> SourceRecords => Set<SourceRecordRow>();

    public DbSet<SourceRecordRevisionRow> SourceRecordRevisions => Set<SourceRecordRevisionRow>();

    public DbSet<ObservationRow> Observations => Set<ObservationRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<SourceRow>(source =>
        {
            source.ToTable("source", t =>
            {
                t.HasCheckConstraint("ck_source_kind", "kind IN ('record', 'advisory', 'signal')");
                t.HasCheckConstraint("ck_source_operator_kind", "operator_kind IN ('authority', 'mandated', 'recognized', 'vendor', 'community')");
                t.HasCheckConstraint("ck_source_terms_mode", "terms_mode IN ('full', 'metadata_only')");
                t.HasCheckConstraint("ck_source_health", "health IN ('healthy', 'degraded', 'failing', 'disabled')");
            });
            source.HasKey(s => s.Id);
            source.Property(s => s.Id).HasDefaultValueSql("uuidv7()");
            source.HasIndex(s => s.Key).IsUnique().HasDatabaseName("ux_source_key");
            source.Property(s => s.Key).HasMaxLength(100);
            source.Property(s => s.Name).HasMaxLength(200);
            source.Property(s => s.Kind).HasMaxLength(20);
            source.Property(s => s.Operator).HasMaxLength(200);
            source.Property(s => s.OperatorKind).HasMaxLength(20);
            source.Property(s => s.Adapter).HasMaxLength(300);
            source.Property(s => s.Config).HasColumnType("jsonb");
            source.Property(s => s.Attribution).HasMaxLength(1000);
            source.Property(s => s.TermsMode).HasMaxLength(20);
            source.Property(s => s.Health).HasMaxLength(20);
            source.Property(s => s.HealthReason).HasMaxLength(1000);
            source.Property(s => s.CreatedAt).HasDefaultValueSql("now()");
            source.HasIndex(s => s.NextSyncAt).HasFilter("enabled").HasDatabaseName("ix_source_next_sync_at");
        });

        modelBuilder.Entity<SourceRunRow>(run =>
        {
            run.ToTable("source_run");
            run.HasKey(r => r.Id);
            run.Property(r => r.Id).HasDefaultValueSql("uuidv7()");
            run.Property(r => r.Mode).HasMaxLength(20);
            run.Property(r => r.Outcome).HasMaxLength(20);
            run.HasOne<SourceRow>().WithMany().HasForeignKey(r => r.SourceId).OnDelete(DeleteBehavior.Cascade);
            run.HasIndex(r => new { r.SourceId, r.StartedAt }).HasDatabaseName("ix_source_run_source_started");
        });

        modelBuilder.Entity<SourceRecordRow>(record =>
        {
            record.ToTable("source_record", t => t.HasCheckConstraint("ck_source_record_parse_status", "parse_status IN ('pending', 'parsed', 'quarantined')"));
            record.HasKey(r => r.Id);
            record.Property(r => r.Id).HasDefaultValueSql("uuidv7()");
            record.Property(r => r.ExternalId).HasMaxLength(300);
            record.Property(r => r.Url).HasMaxLength(2000);
            record.Property(r => r.ContentType).HasMaxLength(100);
            record.Property(r => r.ContentHash).HasMaxLength(64);
            record.Property(r => r.ParseStatus).HasMaxLength(20);
            record.Property(r => r.QuarantineReason).HasMaxLength(2000);
            record.HasIndex(r => new { r.SourceId, r.ExternalId }).IsUnique().HasDatabaseName("ux_source_record_external_id");
            record.HasIndex(r => new { r.SourceId, r.ParseStatus }).HasFilter("parse_status = 'pending'").HasDatabaseName("ix_source_record_pending");
            record.HasOne<SourceRow>().WithMany().HasForeignKey(r => r.SourceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SourceRecordRevisionRow>(revision =>
        {
            revision.ToTable("source_record_revision");
            revision.HasKey(r => r.Id);
            revision.Property(r => r.Id).HasDefaultValueSql("uuidv7()");
            revision.Property(r => r.ContentHash).HasMaxLength(64);
            revision.HasIndex(r => new { r.SourceRecordId, r.Revision }).IsUnique().HasDatabaseName("ux_source_record_revision");
            revision.HasOne<SourceRecordRow>().WithMany().HasForeignKey(r => r.SourceRecordId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ObservationRow>(observation =>
        {
            observation.ToTable("observation", t => t.HasCheckConstraint("ck_observation_kind", "kind IN ('vulnerability', 'advisory', 'warning')"));
            observation.HasKey(o => o.Id);
            observation.Property(o => o.Id).HasDefaultValueSql("uuidv7()");
            observation.HasIndex(o => o.SourceRecordId).IsUnique().HasDatabaseName("ux_observation_source_record");
            observation.Property(o => o.Kind).HasMaxLength(20);
            observation.Property(o => o.Title).HasMaxLength(1000);
            observation.Property(o => o.Language).HasMaxLength(20);
            observation.Property(o => o.Claims).HasColumnType("jsonb");
            observation.Property(o => o.Origin).HasMaxLength(20);
            observation.HasIndex(o => o.Aliases).HasMethod("gin").HasDatabaseName("ix_observation_aliases");
            observation.HasOne<SourceRecordRow>().WithMany().HasForeignKey(o => o.SourceRecordId).OnDelete(DeleteBehavior.Cascade);
            observation.HasOne<SourceRow>().WithMany().HasForeignKey(o => o.SourceId).OnDelete(DeleteBehavior.Cascade);
        });

        ApplyTenantFilters(modelBuilder);
    }
}

internal sealed class IntelDesignTimeFactory : IDesignTimeDbContextFactory<IntelDbContext>
{
    public IntelDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IntelDbContext>().UseArdeaNpgsql("Host=localhost;Database=ardea", IntelDbContext.Schema).Options, new TenantContext());
}
