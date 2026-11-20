using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Catalog.Persistence;

internal sealed class ProductRow
{
    public const string Curated = "curated";
    public const string Learned = "learned";
    public const string Active = "active";
    public const string Withdrawn = "withdrawn";

    public required string Key { get; set; }

    public required string Name { get; set; }

    public string? Vendor { get; set; }

    public required string Kind { get; set; }

    public string[] CpePairs { get; set; } = [];

    public string[] Purls { get; set; } = [];

    public string Aliases { get; set; } = "[]";

    public required string VersionScheme { get; set; }

    public string? PlatformKey { get; set; }

    public string[] RelatedKeys { get; set; } = [];

    public string[] ImageNames { get; set; } = [];

    public bool Noisy { get; set; }

    public required string Origin { get; set; }

    public required string State { get; set; }

    public string? Confidence { get; set; }

    public string? Reasoning { get; set; }

    public string? Evidence { get; set; }

    public string? LearnedBy { get; set; }

    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class UnknownProductRow
{
    public const string Open = "open";

    public const string Pending = "pending";

    public const string Alias = "alias";

    public const string New = "new";

    public const string Unclear = "unclear";

    public const string Rejected = "rejected";

    public const string Withdrawn = "withdrawn";

    public Guid Id { get; set; }

    public required string RawKey { get; set; }

    public string? Vendor { get; set; }

    public required string Product { get; set; }

    public string[] CpePairs { get; set; } = [];

    public string? Purl { get; set; }

    public int RecordCount { get; set; }

    public bool InKev { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public required string Outcome { get; set; }

    public string? ProductKey { get; set; }

    public string? Proposal { get; set; }

    public int Attempts { get; set; }

    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
}

internal sealed class LearningEventRow
{
    public const string Collected = "collected";

    public const string Asked = "asked";

    public const string Learned = "learned";

    public const string Adopted = "adopted";

    public const string Confirmed = "confirmed";

    public const string Rejected = "rejected";

    public const string Withdrawn = "withdrawn";

    public Guid Id { get; set; }

    public DateTimeOffset At { get; set; }

    public required string Kind { get; set; }

    public string? RawKey { get; set; }

    public string? ProductKey { get; set; }

    public Guid? Actor { get; set; }

    public int Calls { get; set; }

    public int Tokens { get; set; }

    public string? Data { get; set; }
}

internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public const string Schema = "catalog";

    public DbSet<ProductRow> Products => Set<ProductRow>();

    public DbSet<UnknownProductRow> UnknownProducts => Set<UnknownProductRow>();

    public DbSet<LearningEventRow> LearningEvents => Set<LearningEventRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ProductRow>(product =>
        {
            product.ToTable("product", t =>
            {
                t.HasCheckConstraint("ck_product_origin", "origin IN ('curated', 'learned')");
                t.HasCheckConstraint("ck_product_state", "state IN ('active', 'pending', 'rejected', 'withdrawn')");
                t.HasCheckConstraint("ck_product_curated_active", "origin <> 'curated' OR state = 'active'");
                t.HasCheckConstraint("ck_product_kind", "kind IN ('os', 'runtime', 'framework', 'cms', 'database', 'messaging', 'container', 'network', 'virtualization', 'library', 'plugin', 'service', 'other')");
                t.HasCheckConstraint("ck_product_confidence", "confidence IS NULL OR confidence IN ('high', 'medium', 'low')");
                t.HasCheckConstraint("ck_product_learned_by", "learned_by IS NULL OR learned_by IN ('rule', 'model', 'admin')");
            });
            product.HasKey(p => p.Key);
            product.Property(p => p.Key).HasMaxLength(200);
            product.Property(p => p.Name).HasMaxLength(200);
            product.Property(p => p.Vendor).HasMaxLength(200);
            product.Property(p => p.Kind).HasMaxLength(20);
            product.Property(p => p.Aliases).HasColumnType("jsonb");
            product.Property(p => p.VersionScheme).HasMaxLength(20);
            product.Property(p => p.PlatformKey).HasMaxLength(200);
            product.Property(p => p.Origin).HasMaxLength(10);
            product.Property(p => p.State).HasMaxLength(10);
            product.Property(p => p.Confidence).HasMaxLength(10);
            product.Property(p => p.Evidence).HasColumnType("jsonb");
            product.Property(p => p.LearnedBy).HasMaxLength(10);
            product.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
            product.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");
            product.HasIndex(p => new { p.Origin, p.State }).HasDatabaseName("ix_product_origin_state");
        });

        modelBuilder.Entity<UnknownProductRow>(unknown =>
        {
            unknown.ToTable("unknown_product", t =>
                t.HasCheckConstraint("ck_unknown_product_outcome", "outcome IN ('open', 'pending', 'alias', 'new', 'unclear', 'rejected', 'withdrawn')"));
            unknown.HasKey(u => u.Id);
            unknown.Property(u => u.Id).HasDefaultValueSql("uuidv7()");
            unknown.Property(u => u.RawKey).HasMaxLength(200);
            unknown.HasIndex(u => u.RawKey).IsUnique().HasDatabaseName("ux_unknown_product_raw_key");
            unknown.Property(u => u.Vendor).HasMaxLength(200);
            unknown.Property(u => u.Product).HasMaxLength(200);
            unknown.Property(u => u.Purl).HasMaxLength(500);
            unknown.Property(u => u.Outcome).HasMaxLength(10).HasDefaultValue("open");
            unknown.Property(u => u.ProductKey).HasMaxLength(200);
            unknown.Property(u => u.FirstSeenAt).HasDefaultValueSql("now()");
            unknown.HasIndex(u => new { u.Outcome, u.InKev, u.RecordCount, u.RawKey }).IsDescending(false, true, true, false).HasDatabaseName("ix_unknown_product_outcome_count");
            unknown.Property(u => u.Proposal).HasColumnType("jsonb");
            unknown.Property(u => u.Attempts).HasDefaultValue(0);
        });

        modelBuilder.Entity<LearningEventRow>(e =>
        {
            e.ToTable("learning_event", t =>
                t.HasCheckConstraint("ck_learning_event_kind", "kind IN ('collected', 'asked', 'learned', 'adopted', 'confirmed', 'rejected', 'withdrawn')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("uuidv7()");
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.RawKey).HasMaxLength(200);
            e.Property(x => x.ProductKey).HasMaxLength(200);
            e.Property(x => x.Data).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Kind, x.At }).HasDatabaseName("ix_learning_event_kind_at");
        });

        ApplyTenantFilters(modelBuilder);
    }
}

internal sealed class CatalogDesignTimeFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseArdeaNpgsql("Host=localhost;Database=ardea", CatalogDbContext.Schema).Options, new TenantContext());
}
