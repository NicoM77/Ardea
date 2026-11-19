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

    public DbSet<EntryRow> Entries => Set<EntryRow>();

    public DbSet<EntryAliasRow> EntryAliases => Set<EntryAliasRow>();

    public DbSet<EntryAffectedRow> EntryAffected => Set<EntryAffectedRow>();

    public DbSet<EntryReferenceRow> EntryReferences => Set<EntryReferenceRow>();

    public DbSet<EntrySourceRow> EntrySources => Set<EntrySourceRow>();

    public DbSet<EntryChangeRow> EntryChanges => Set<EntryChangeRow>();

    public DbSet<EntrySignalRow> EntrySignals => Set<EntrySignalRow>();

    public DbSet<ReviewItemRow> ReviewItems => Set<ReviewItemRow>();

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

        modelBuilder.Entity<EntryRow>(entry =>
        {
            entry.ToTable("security_entry", t =>
            {
                t.HasCheckConstraint("ck_security_entry_kind", "kind IN ('vulnerability', 'warning')");
                t.HasCheckConstraint("ck_security_entry_state", "state IN ('published', 'rejected', 'disputed', 'merged')");
                t.HasCheckConstraint("ck_security_entry_exploitation", "exploitation IN ('none', 'poc', 'active')");
            });
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
            entry.HasIndex(e => e.PrimaryKey).IsUnique().HasDatabaseName("ux_security_entry_primary_key");
            entry.HasIndex(e => e.ChangedAt).IsDescending().HasDatabaseName("ix_security_entry_changed_at");
            entry.Property(e => e.Kind).HasMaxLength(20);
            entry.Property(e => e.PrimaryKey).HasMaxLength(300);
            entry.Property(e => e.Title).HasMaxLength(1000);
            entry.Property(e => e.State).HasMaxLength(20);
            entry.Property(e => e.MaterialFingerprint).HasMaxLength(64);
            entry.Property(e => e.RemediationFingerprint).HasMaxLength(64);
            entry.Property(e => e.CvssVersion).HasMaxLength(10);
            entry.Property(e => e.CvssVector).HasMaxLength(300);
            entry.Property(e => e.Exploitation).HasMaxLength(10);
            entry.Property(e => e.ExploitationEvidence).HasColumnType("jsonb");
            entry.Property(e => e.EpssClass).HasMaxLength(10);
            entry.Property(e => e.Provenance).HasColumnType("jsonb");
            entry.Property(e => e.Details).HasColumnType("jsonb");
        });

        modelBuilder.Entity<EntryAliasRow>(alias =>
        {
            alias.ToTable("entry_alias");
            alias.HasKey(a => a.Id);
            alias.Property(a => a.Id).HasDefaultValueSql("uuidv7()");
            alias.Property(a => a.Scheme).HasMaxLength(50);
            alias.Property(a => a.Value).HasMaxLength(300);
            alias.HasIndex(a => new { a.Scheme, a.Value }).IsUnique().HasDatabaseName("ux_entry_alias");
            alias.HasIndex(a => a.EntryId).HasDatabaseName("ix_entry_alias_entry");
            alias.HasOne<EntryRow>().WithMany().HasForeignKey(a => a.EntryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntryAffectedRow>(affected =>
        {
            affected.ToTable("entry_affected", t =>
            {
                t.HasCheckConstraint("ck_entry_affected_status", "status IN ('affected', 'unaffected', 'unknown')");
                t.HasCheckConstraint("ck_entry_affected_default_status", "default_status IN ('affected', 'unaffected', 'unknown')");
            });
            affected.HasKey(a => a.Id);
            affected.Property(a => a.Id).HasDefaultValueSql("uuidv7()");
            foreach (var name in new[] { nameof(EntryAffectedRow.Vendor), nameof(EntryAffectedRow.Product), nameof(EntryAffectedRow.ProductKey), nameof(EntryAffectedRow.LearnedFrom), nameof(EntryAffectedRow.CpeVendor), nameof(EntryAffectedRow.CpeProduct), nameof(EntryAffectedRow.PurlName) })
            {
                affected.Property(name).HasMaxLength(500);
            }

            affected.Property(a => a.PurlType).HasMaxLength(50);
            affected.Property(a => a.VersionScheme).HasMaxLength(50);
            affected.Property(a => a.Status).HasMaxLength(20);
            affected.Property(a => a.DefaultStatus).HasMaxLength(20);
            // The indexes the candidate search uses (master plan 4.4).
            affected.HasIndex(a => a.ProductKey).HasDatabaseName("ix_entry_affected_product_key");
            // The raw keys learned entries resolved: the reverse search and a withdrawal find them (master plan 6.9).
            affected.HasIndex(a => a.LearnedFrom).HasDatabaseName("ix_entry_affected_learned_from").HasFilter("learned_from IS NOT NULL");
            affected.HasIndex(a => new { a.CpeVendor, a.CpeProduct }).HasDatabaseName("ix_entry_affected_cpe");
            affected.HasIndex(a => new { a.PurlType, a.PurlName }).HasDatabaseName("ix_entry_affected_purl");
            affected.HasIndex(a => a.EntryId).HasDatabaseName("ix_entry_affected_entry");
            affected.HasOne<EntryRow>().WithMany().HasForeignKey(a => a.EntryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntryReferenceRow>(reference =>
        {
            reference.ToTable("entry_reference");
            reference.HasKey(r => r.Id);
            reference.Property(r => r.Id).HasDefaultValueSql("uuidv7()");
            reference.Property(r => r.Url).HasMaxLength(2000);
            reference.HasIndex(r => r.EntryId).HasDatabaseName("ix_entry_reference_entry");
            reference.HasOne<EntryRow>().WithMany().HasForeignKey(r => r.EntryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntrySourceRow>(link =>
        {
            link.ToTable("entry_source", t => t.HasCheckConstraint("ck_entry_source_relation", "relation IN ('describes', 'mentions', 'signal')"));
            link.HasKey(l => l.Id);
            link.Property(l => l.Id).HasDefaultValueSql("uuidv7()");
            link.Property(l => l.Relation).HasMaxLength(20);
            link.Property(l => l.Url).HasMaxLength(2000);
            link.HasIndex(l => l.EntryId).HasDatabaseName("ix_entry_source_entry");
            link.HasOne<EntryRow>().WithMany().HasForeignKey(l => l.EntryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntryChangeRow>(change =>
        {
            change.ToTable("entry_change");
            change.HasKey(c => c.Id);
            change.Property(c => c.Id).HasDefaultValueSql("uuidv7()");
            change.Property(c => c.Diff).HasColumnType("jsonb");
            change.Property(c => c.TriggeredBy).HasMaxLength(100);
            change.HasIndex(c => new { c.EntryId, c.Revision }).IsUnique().HasDatabaseName("ux_entry_change_revision");
            change.HasOne<EntryRow>().WithMany().HasForeignKey(c => c.EntryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntrySignalRow>(signal =>
        {
            signal.ToTable("entry_signal", t => t.HasCheckConstraint("ck_entry_signal_epss_class", "epss_class IN ('high', 'low')"));
            signal.HasKey(s => s.Id);
            signal.Property(s => s.Id).HasDefaultValueSql("uuidv7()");
            signal.Property(s => s.Scheme).HasMaxLength(50);
            signal.Property(s => s.Value).HasMaxLength(300);
            signal.Property(s => s.KevUrl).HasMaxLength(500);
            signal.Property(s => s.EpssClass).HasMaxLength(10);
            signal.HasIndex(s => new { s.Scheme, s.Value }).IsUnique().HasDatabaseName("ux_entry_signal");
        });

        modelBuilder.Entity<ReviewItemRow>(item =>
        {
            item.ToTable("review_item", t =>
            {
                t.HasCheckConstraint("ck_review_item_kind", "kind IN ('cve_conflict')");
                t.HasCheckConstraint("ck_review_item_state", "state IN ('open', 'kept_separate')");
            });
            item.HasKey(i => i.Id);
            item.Property(i => i.Id).HasDefaultValueSql("uuidv7()");
            item.Property(i => i.Kind).HasMaxLength(30);
            item.Property(i => i.SubjectKey).HasMaxLength(1000);
            item.Property(i => i.State).HasMaxLength(20);
            item.HasIndex(i => new { i.Kind, i.SubjectKey }).IsUnique().HasDatabaseName("ux_review_item_subject");
            item.HasIndex(i => i.State).HasDatabaseName("ix_review_item_state");
        });

        ApplyTenantFilters(modelBuilder);
    }
}

internal sealed class IntelDesignTimeFactory : IDesignTimeDbContextFactory<IntelDbContext>
{
    public IntelDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IntelDbContext>().UseArdeaNpgsql("Host=localhost;Database=ardea", IntelDbContext.Schema).Options, new TenantContext());
}
