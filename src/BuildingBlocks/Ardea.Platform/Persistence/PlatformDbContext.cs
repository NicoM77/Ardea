using Ardea.Platform.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Platform.Persistence;

/// <summary>Tables of the <c>platform</c> schema (master plan 4.8). All of them are global.</summary>
internal sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : ModuleDbContext(options, tenant: null)
{
    public const string Schema = "platform";

    public DbSet<JobRow> Jobs => Set<JobRow>();

    public DbSet<ScheduleRow> Schedules => Set<ScheduleRow>();

    public DbSet<SettingRow> Settings => Set<SettingRow>();

    public DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();

    public DbSet<DataProtectionKeyRow> DataProtectionKeys => Set<DataProtectionKeyRow>();

    public DbSet<WorkerHeartbeatRow> WorkerHeartbeats => Set<WorkerHeartbeatRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<JobRow>(job =>
        {
            job.ToTable("job");
            job.HasKey(j => j.Id);
            job.Property(j => j.Id).HasDefaultValueSql("uuidv7()");
            job.Property(j => j.Kind).HasMaxLength(100);
            job.Property(j => j.State)
                .HasMaxLength(20)
                .HasConversion(s => JobStates.ToDatabase(s), s => JobStates.FromDatabase(s));
            job.ToTable(t => t.HasCheckConstraint(
                "ck_job_state", "state IN ('pending', 'running', 'succeeded', 'failed', 'dead')"));
            job.Property(j => j.CoalesceKey).HasMaxLength(200);
            job.Property(j => j.Payload).HasColumnType("jsonb");
            job.Property(j => j.LeaseOwner).HasMaxLength(200);
            job.Property(j => j.TraceParent).HasMaxLength(100);
            job.Property(j => j.CreatedAt).HasDefaultValueSql("now()");
            // Fetch: pending jobs that are due, by priority and due time.
            job.HasIndex(j => new { j.Priority, j.RunAt })
                .HasFilter("state = 'pending'")
                .HasDatabaseName("ix_job_due");
            // At most one waiting job per coalesce key; running or finished jobs do not block a new one.
            job.HasIndex(j => j.CoalesceKey)
                .IsUnique()
                .HasFilter("state = 'pending' AND coalesce_key IS NOT NULL")
                .HasDatabaseName("ux_job_pending_coalesce_key");
            job.HasIndex(j => j.LeaseUntil)
                .HasFilter("state = 'running'")
                .HasDatabaseName("ix_job_running_lease");
            job.HasIndex(j => j.FinishedAt)
                .HasFilter("state IN ('succeeded', 'failed', 'dead')")
                .HasDatabaseName("ix_job_finished");
        });

        modelBuilder.Entity<ScheduleRow>(schedule =>
        {
            schedule.ToTable("schedule");
            schedule.HasKey(s => s.Key);
            schedule.Property(s => s.Key).HasMaxLength(100);
            schedule.Property(s => s.JobKind).HasMaxLength(100);
            schedule.Property(s => s.Cron).HasMaxLength(100);
            schedule.Property(s => s.TimeZone).HasMaxLength(64);
        });

        modelBuilder.Entity<SettingRow>(setting =>
        {
            setting.ToTable("setting");
            setting.HasKey(s => s.Key);
            setting.Property(s => s.Key).HasMaxLength(200);
            setting.Property(s => s.Value).HasColumnType("jsonb");
        });

        modelBuilder.Entity<AuditEventRow>(audit =>
        {
            audit.ToTable("audit_event");
            audit.HasKey(a => a.Id);
            audit.Property(a => a.Id).HasDefaultValueSql("uuidv7()");
            audit.Property(a => a.Action).HasMaxLength(100);
            audit.Property(a => a.SubjectType).HasMaxLength(100);
            audit.Property(a => a.SubjectId).HasMaxLength(200);
            audit.Property(a => a.Origin).HasMaxLength(200);
            audit.Property(a => a.IpAddress).HasMaxLength(64);
            audit.Property(a => a.Data).HasColumnType("jsonb");
            audit.Property(a => a.At).HasDefaultValueSql("now()");
            audit.HasIndex(a => new { a.OrganizationId, a.At }).HasDatabaseName("ix_audit_event_org_at");
        });

        modelBuilder.Entity<DataProtectionKeyRow>(key =>
        {
            key.ToTable("data_protection_key");
            key.HasKey(k => k.Id);
        });

        modelBuilder.Entity<WorkerHeartbeatRow>(beat =>
        {
            beat.ToTable("worker_heartbeat");
            beat.HasKey(b => b.WorkerId);
            beat.Property(b => b.WorkerId).HasMaxLength(200);
            beat.Property(b => b.Version).HasMaxLength(50);
        });
    }
}
