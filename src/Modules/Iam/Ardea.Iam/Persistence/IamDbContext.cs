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

    private static readonly System.Text.Json.JsonSerializerOptions SettingsJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    public DbSet<ArdeaUser> Users => Set<ArdeaUser>();

    public DbSet<IdentityUserClaim<Guid>> UserClaims => Set<IdentityUserClaim<Guid>>();

    public DbSet<IdentityUserLogin<Guid>> UserLogins => Set<IdentityUserLogin<Guid>>();

    public DbSet<IdentityUserToken<Guid>> UserTokens => Set<IdentityUserToken<Guid>>();

    public DbSet<UserSession> Sessions => Set<UserSession>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

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

        modelBuilder.Entity<Organization>(organization =>
        {
            organization.ToTable("organization", t => t.HasCheckConstraint("ck_organization_slug", "slug ~ '^[a-z0-9]([a-z0-9-]{0,48}[a-z0-9])?$'"));
            organization.HasKey(o => o.OrganizationId);
            organization.Property(o => o.OrganizationId).HasColumnName("id");
            organization.Property(o => o.Name).HasMaxLength(200);
            organization.Property(o => o.Slug).HasMaxLength(50);
            organization.Property(o => o.Locale).HasMaxLength(10);
            organization.Property(o => o.TimeZone).HasMaxLength(64);
            organization.Property(o => o.Settings).HasColumnType("jsonb").HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, SettingsJson),
                v => System.Text.Json.JsonSerializer.Deserialize<OrganizationSettings>(v, SettingsJson) ?? new OrganizationSettings());
            organization.HasIndex(o => o.Slug).IsUnique().HasDatabaseName("ux_organization_slug");
        });

        modelBuilder.Entity<Membership>(membership =>
        {
            membership.ToTable("membership", t => t.HasCheckConstraint("ck_membership_role", "role IN ('owner', 'admin', 'member', 'viewer')"));
            membership.HasKey(m => m.Id);
            membership.Property(m => m.Id).HasDefaultValueSql("uuidv7()");
            membership.Property(m => m.Role).HasMaxLength(20);
            membership.HasIndex(m => new { m.OrganizationId, m.UserId }).IsUnique().HasDatabaseName("ux_membership_org_user");
            membership.HasIndex(m => m.UserId).HasDatabaseName("ix_membership_user");
            // At most one owner; the transfer changes both rows in one unit of work.
            membership.HasIndex(m => m.OrganizationId).IsUnique().HasFilter("role = 'owner'").HasDatabaseName("ux_membership_owner");
            membership.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            membership.HasOne<ArdeaUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Invitation>(invitation =>
        {
            invitation.ToTable("invitation", t => t.HasCheckConstraint("ck_invitation_role", "role IN ('admin', 'member', 'viewer')"));
            invitation.HasKey(i => i.Id);
            invitation.Property(i => i.Id).HasDefaultValueSql("uuidv7()");
            invitation.Property(i => i.Email).HasMaxLength(256);
            invitation.Property(i => i.NormalizedEmail).HasMaxLength(256);
            invitation.Property(i => i.Role).HasMaxLength(20);
            invitation.Property(i => i.TokenHash).HasMaxLength(64);
            invitation.HasIndex(i => i.TokenHash).IsUnique().HasDatabaseName("ux_invitation_token");
            invitation.HasIndex(i => new { i.OrganizationId, i.NormalizedEmail }).IsUnique().HasDatabaseName("ux_invitation_org_email");
            invitation.HasOne<Organization>().WithMany().HasForeignKey(i => i.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            invitation.HasOne<ArdeaUser>().WithMany().HasForeignKey(i => i.InvitedBy).OnDelete(DeleteBehavior.Cascade);
        });

        ApplyTenantFilters(modelBuilder);
    }
}

internal sealed class IamDesignTimeFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<IamDbContext>
{
    public IamDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IamDbContext>().UseArdeaNpgsql("Host=localhost;Database=ardea", IamDbContext.Schema).Options, new TenantContext());
}
