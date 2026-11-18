using Ardea.Iam.Domain;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Iam.Persistence;

/// <summary>
/// Schema <c>iam</c>. The Identity tables are mapped here by hand instead of inheriting
/// <c>IdentityDbContext</c>, so the context is a <see cref="ModuleDbContext"/> and writes inside the
/// unit of work like every other module (ADR 023).
/// </summary>
internal sealed class IamDbContext(DbContextOptions<IamDbContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public const string Schema = "iam";

    public DbSet<ArdeaUser> Users => Set<ArdeaUser>();

    public DbSet<IdentityUserClaim<Guid>> UserClaims => Set<IdentityUserClaim<Guid>>();

    public DbSet<IdentityUserLogin<Guid>> UserLogins => Set<IdentityUserLogin<Guid>>();

    public DbSet<IdentityUserToken<Guid>> UserTokens => Set<IdentityUserToken<Guid>>();

    public DbSet<UserSession> Sessions => Set<UserSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ArdeaUser>(user =>
        {
            user.ToTable("user");
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).HasDefaultValueSql("uuidv7()");
            user.HasIndex(u => u.NormalizedEmail).IsUnique().HasDatabaseName("ux_user_normalized_email");
            user.HasIndex(u => u.NormalizedUserName).IsUnique().HasDatabaseName("ux_user_normalized_user_name");
            user.Property(u => u.ConcurrencyStamp).IsConcurrencyToken();
            user.Property(u => u.UserName).HasMaxLength(256);
            user.Property(u => u.NormalizedUserName).HasMaxLength(256);
            user.Property(u => u.Email).HasMaxLength(256);
            user.Property(u => u.NormalizedEmail).HasMaxLength(256);
            user.Property(u => u.DisplayName).HasMaxLength(200);
            user.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
            user.HasMany<IdentityUserClaim<Guid>>().WithOne().HasForeignKey(c => c.UserId).IsRequired();
            user.HasMany<IdentityUserLogin<Guid>>().WithOne().HasForeignKey(l => l.UserId).IsRequired();
            user.HasMany<IdentityUserToken<Guid>>().WithOne().HasForeignKey(t => t.UserId).IsRequired();
            user.HasMany<UserSession>().WithOne().HasForeignKey(s => s.UserId).IsRequired();
        });

        modelBuilder.Entity<IdentityUserClaim<Guid>>(claim =>
        {
            claim.ToTable("user_claim");
            claim.HasKey(c => c.Id);
        });

        modelBuilder.Entity<IdentityUserLogin<Guid>>(login =>
        {
            login.ToTable("user_login");
            login.HasKey(l => new { l.LoginProvider, l.ProviderKey });
            login.Property(l => l.LoginProvider).HasMaxLength(128);
            login.Property(l => l.ProviderKey).HasMaxLength(128);
        });

        modelBuilder.Entity<IdentityUserToken<Guid>>(token =>
        {
            token.ToTable("user_token");
            token.HasKey(t => new { t.UserId, t.LoginProvider, t.Name });
            token.Property(t => t.LoginProvider).HasMaxLength(128);
            token.Property(t => t.Name).HasMaxLength(128);
        });

        modelBuilder.Entity<UserSession>(session =>
        {
            session.ToTable("session");
            session.HasKey(s => s.Id);
            session.Property(s => s.UserAgent).HasMaxLength(500);
            session.Property(s => s.IpAddress).HasMaxLength(64);
            session.HasIndex(s => new { s.UserId, s.CreatedAt }).HasDatabaseName("ix_session_user");
        });

        ApplyTenantFilters(modelBuilder);
    }
}

internal sealed class IamDesignTimeFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<IamDbContext>
{
    public IamDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IamDbContext>().UseArdeaNpgsql("Host=localhost;Database=ardea", IamDbContext.Schema).Options, new TenantContext());
}
