using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Catalog.Persistence;

/// <summary>A row of <c>catalog.product</c> (master plan 4.5): curated entries and learned ones (6.9).</summary>
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

    /// <summary>CPE <c>vendor:product</c> pairs in the canonical form of the formatted string.</summary>
    public string[] CpePairs { get; set; } = [];

    /// <summary>Package URLs without version, e.g. <c>pkg:npm/express</c>.</summary>
    public string[] Purls { get; set; } = [];

    /// <summary>JSON list of <c>{ "vendor": …, "names": [ … ] }</c>.</summary>
    public string Aliases { get; set; } = "[]";

    public required string VersionScheme { get; set; }

    public string? PlatformKey { get; set; }

    public string[] RelatedKeys { get; set; } = [];

    public string[] ImageNames { get; set; } = [];

    /// <summary><c>curated</c> or <c>learned</c>.</summary>
    public required string Origin { get; set; }

    /// <summary><c>active</c>, <c>pending</c>, <c>rejected</c> or <c>withdrawn</c>; always <c>active</c> for curated entries.</summary>
    public required string State { get; set; }

    /// <summary>For learned entries: the model's confidence (<c>high</c>, <c>medium</c>, <c>low</c>).</summary>
    public string? Confidence { get; set; }

    public string? Reasoning { get; set; }

    /// <summary>For learned entries: raw statements and records the entry was learned from (JSON).</summary>
    public string? Evidence { get; set; }

    /// <summary>For learned entries: <c>rule</c>, <c>model</c> or <c>admin</c>.</summary>
    public string? LearnedBy { get; set; }

    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A row of <c>catalog.unknown_product</c>: a product statement of a source without catalogue entry (master plan 4.5, 6.9).</summary>
internal sealed class UnknownProductRow
{
    /// <summary>Collected, not decided yet.</summary>
    public const string Open = "open";

    /// <summary>A proposal waits on the review list.</summary>
    public const string Pending = "pending";

    /// <summary>In effect: the raw key resolves to <see cref="ProductKey"/>, an existing entry.</summary>
    public const string Alias = "alias";

    /// <summary>In effect: the raw key resolves to the learned entry <see cref="ProductKey"/> made from it.</summary>
    public const string New = "new";

    /// <summary>The model could not say, or its answers failed the check twice; on the review list.</summary>
    public const string Unclear = "unclear";

    /// <summary>Rejected by the administrator; never proposed again.</summary>
    public const string Rejected = "rejected";

    /// <summary>Was in effect and was taken back; never proposed again.</summary>
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

    /// <summary><c>open</c>, <c>alias</c>, <c>new</c>, <c>unclear</c> or <c>rejected</c>.</summary>
    public required string Outcome { get; set; }

    /// <summary>For <c>alias</c> and <c>new</c>: the catalogue key the raw key now resolves to.</summary>
    public string? ProductKey { get; set; }

    /// <summary>What was learned about the raw key (<c>LearnedProposal</c> as JSON): by rule, model or administrator.</summary>
    public string? Proposal { get; set; }

    /// <summary>Calls to a language model made for this raw key.</summary>
    public int Attempts { get; set; }

    /// <summary>The administrator who decided; <c>null</c> for a decision of the system.</summary>
    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
}

/// <summary>
/// A row of <c>catalog.learning_event</c>: what happened while the catalogue learned (master plan 6.9 «Messgrössen»),
/// one row per collection, model call, proposal and decision. Counts only, never texts of a model.
/// </summary>
internal sealed class LearningEventRow
{
    /// <summary>A collection of unknown products; <see cref="Data"/> holds the counts.</summary>
    public const string Collected = "collected";

    /// <summary>The model was asked about a raw key; <see cref="Calls"/> and <see cref="Tokens"/> count it.</summary>
    public const string Asked = "asked";

    /// <summary>Something was learned about a raw key, by rule or model.</summary>
    public const string Learned = "learned";

    /// <summary>A learned entry took effect without a person.</summary>
    public const string Adopted = "adopted";

    /// <summary>The administrator confirmed (or changed and confirmed) a proposal.</summary>
    public const string Confirmed = "confirmed";

    public const string Rejected = "rejected";

    /// <summary>A learned entry in effect was taken back.</summary>
    public const string Withdrawn = "withdrawn";

    public Guid Id { get; set; }

    public DateTimeOffset At { get; set; }

    public required string Kind { get; set; }

    public string? RawKey { get; set; }

    public string? ProductKey { get; set; }

    public Guid? Actor { get; set; }

    public int Calls { get; set; }

    public int Tokens { get; set; }

    /// <summary>JSON with counts, e.g. of a collection.</summary>
    public string? Data { get; set; }
}

/// <summary>Schema <c>catalog</c>: global data without organisation, readable by every role.</summary>
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
            // The daily collection works through the most frequent open keys first (master plan 6.9).
            unknown.HasIndex(u => new { u.Outcome, u.RecordCount }).HasDatabaseName("ix_unknown_product_outcome_count");
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
            // The weekly measures and the daily call quota read by kind and time.
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
