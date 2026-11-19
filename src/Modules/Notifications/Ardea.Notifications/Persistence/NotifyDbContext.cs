using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ardea.Notifications.Persistence;

/// <summary>
/// Schema <c>notify</c> (master plan 4.7): organisation data, two user tables that also carry the
/// organisation (<c>notification</c>, <c>user_preference</c>), and the devices' push subscriptions, which do not.
/// </summary>
internal sealed class NotifyDbContext(DbContextOptions<NotifyDbContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public const string Schema = "notify";

    public DbSet<NotificationRule> Rules => Set<NotificationRule>();

    public DbSet<RuleTarget> Targets => Set<RuleTarget>();

    public DbSet<WebhookEndpoint> Endpoints => Set<WebhookEndpoint>();

    public DbSet<ExternalAddress> Addresses => Set<ExternalAddress>();

    public DbSet<RecipientState> States => Set<RecipientState>();

    public DbSet<Delivery> Deliveries => Set<Delivery>();

    public DbSet<DeliveryEvent> DeliveryEvents => Set<DeliveryEvent>();

    public DbSet<DigestItem> DigestItems => Set<DigestItem>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<UserPreference> Preferences => Set<UserPreference>();

    public DbSet<PendingEvent> PendingEvents => Set<PendingEvent>();

    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<NotificationRule>(rule =>
        {
            rule.ToTable("rule", t => t.HasCheckConstraint("ck_rule_min_level", "min_level BETWEEN 2 AND 5"));
            rule.HasKey(r => r.Id);
            rule.Property(r => r.Id).HasDefaultValueSql("uuidv7()");
            rule.Property(r => r.Name).HasMaxLength(200);
            rule.Property(r => r.Preset).HasMaxLength(20);
            rule.HasIndex(r => r.OrganizationId).HasDatabaseName("ix_rule_org");
            rule.HasMany(r => r.Targets).WithOne().HasForeignKey(t => t.RuleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RuleTarget>(target =>
        {
            target.ToTable("rule_target", t =>
            {
                t.HasCheckConstraint("ck_rule_target_channel", "channel IN ('in_app', 'email', 'webhook', 'push')");
                t.HasCheckConstraint("ck_rule_target_recipient", "recipient_kind IN ('role', 'user', 'address', 'endpoint')");
                t.HasCheckConstraint("ck_rule_target_mode", "mode IN ('immediate', 'digest') AND (mode = 'immediate' OR digest_cron IS NOT NULL)");
            });
            target.HasKey(t => t.Id);
            target.Property(t => t.Id).HasDefaultValueSql("uuidv7()");
            target.Property(t => t.Channel).HasMaxLength(20);
            target.Property(t => t.RecipientKind).HasMaxLength(20);
            target.Property(t => t.RecipientRef).HasMaxLength(100);
            target.Property(t => t.Mode).HasMaxLength(20);
            target.Property(t => t.DigestCron).HasMaxLength(100);
            target.HasIndex(t => t.RuleId).HasDatabaseName("ix_rule_target_rule");
            // The instance's digest schedule asks which targets are due (master plan 8.4, 9.2).
            target.HasIndex(t => t.NextDigestAt).HasFilter("mode = 'digest'").HasDatabaseName("ix_rule_target_due");
        });

        modelBuilder.Entity<WebhookEndpoint>(endpoint =>
        {
            endpoint.ToTable("endpoint");
            endpoint.HasKey(e => e.Id);
            endpoint.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
            endpoint.Property(e => e.Name).HasMaxLength(200);
            endpoint.Property(e => e.LastStatus).HasMaxLength(100);
            endpoint.HasIndex(e => e.OrganizationId).HasDatabaseName("ix_endpoint_org");
        });

        modelBuilder.Entity<ExternalAddress>(address =>
        {
            address.ToTable("external_address");
            address.HasKey(a => a.Id);
            address.Property(a => a.Id).HasDefaultValueSql("uuidv7()");
            address.Property(a => a.Address).HasMaxLength(320);
            address.Property(a => a.NormalizedAddress).HasMaxLength(320);
            address.Property(a => a.TokenHash).HasMaxLength(64);
            address.HasIndex(a => new { a.OrganizationId, a.NormalizedAddress }).IsUnique().HasDatabaseName("ux_external_address");
        });

        modelBuilder.Entity<RecipientState>(state =>
        {
            state.ToTable("recipient_state", t => t.HasCheckConstraint("ck_recipient_state_exploitation", "last_exploitation IN ('none', 'poc', 'active')"));
            state.HasKey(s => new { s.OrganizationId, s.RemediationId, s.RecipientKey, s.Channel });
            state.Property(s => s.RecipientKey).HasMaxLength(100);
            state.Property(s => s.Channel).HasMaxLength(20);
            state.Property(s => s.LastExploitation).HasMaxLength(10);
        });

        modelBuilder.Entity<Delivery>(delivery =>
        {
            delivery.ToTable("delivery", t =>
            {
                t.HasCheckConstraint("ck_delivery_kind", "kind IN ('immediate', 'digest')");
                t.HasCheckConstraint("ck_delivery_state", "state IN ('pending', 'sending', 'sent', 'failed', 'dead', 'suppressed')");
            });
            delivery.HasKey(d => d.Id);
            delivery.Property(d => d.Id).HasDefaultValueSql("uuidv7()");
            delivery.Property(d => d.Kind).HasMaxLength(20);
            delivery.Property(d => d.Channel).HasMaxLength(20);
            delivery.Property(d => d.RecipientKey).HasMaxLength(100);
            delivery.Property(d => d.State).HasMaxLength(20);
            delivery.Property(d => d.LastError).HasMaxLength(2000);
            // One digest per target, recipient and period (master plan 8.4).
            delivery.HasIndex(d => new { d.RuleTargetId, d.RecipientKey, d.PeriodEnd }).IsUnique().HasFilter("kind = 'digest'").HasDatabaseName("ux_delivery_digest");
            // At most one waiting immediate message per recipient and channel: further events join it (master plan 8.4).
            delivery.HasIndex(d => new { d.OrganizationId, d.RecipientKey, d.Channel }).IsUnique()
                .HasFilter("kind = 'immediate' AND state = 'pending'").HasDatabaseName("ux_delivery_waiting");
            // The log, newest first, and the hourly limit per channel (master plan 8.6).
            delivery.HasIndex(d => new { d.OrganizationId, d.CreatedAt }).HasDatabaseName("ix_delivery_org_created");
            delivery.HasOne<RuleTarget>().WithMany().HasForeignKey(d => d.RuleTargetId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DeliveryEvent>(e =>
        {
            e.ToTable("delivery_event");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("uuidv7()");
            e.Property(x => x.RecipientKey).HasMaxLength(100);
            e.Property(x => x.Channel).HasMaxLength(20);
            e.Property(x => x.Event).HasMaxLength(40);
            // Recipient, channel and event together are unique: an event processed again sends nothing (master plan 8.4).
            // A warning's event is its entry, the same in every organisation; the organisation is part of the key.
            e.HasIndex(x => new { x.OrganizationId, x.RecipientKey, x.Channel, x.EventId }).IsUnique().HasDatabaseName("ux_delivery_event");
            e.HasIndex(x => x.DeliveryId).HasDatabaseName("ix_delivery_event_delivery");
            e.HasOne<Delivery>().WithMany().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DigestItem>(item =>
        {
            item.ToTable("digest_item");
            item.HasKey(i => i.Id);
            item.Property(i => i.Id).HasDefaultValueSql("uuidv7()");
            item.Property(i => i.Event).HasMaxLength(40);
            item.HasIndex(i => new { i.RuleTargetId, i.EventId }).IsUnique().HasDatabaseName("ux_digest_item");
            item.HasIndex(i => new { i.RuleTargetId, i.PeriodEnd }).HasDatabaseName("ix_digest_item_period");
            item.HasOne<RuleTarget>().WithMany().HasForeignKey(i => i.RuleTargetId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Notification>(notification =>
        {
            notification.ToTable("notification");
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Id).HasDefaultValueSql("uuidv7()");
            notification.Property(n => n.Kind).HasMaxLength(40);
            notification.Property(n => n.Title).HasMaxLength(300);
            notification.Property(n => n.Body).HasMaxLength(2000);
            notification.Property(n => n.Link).HasMaxLength(500);
            notification.HasIndex(n => new { n.UserId, n.OrganizationId, n.EventId }).IsUnique().HasDatabaseName("ux_notification_event");
            // The member's list and unread counter in one organisation, newest first.
            notification.HasIndex(n => new { n.OrganizationId, n.UserId, n.CreatedAt }).HasDatabaseName("ix_notification_user");
        });

        modelBuilder.Entity<UserPreference>(preference =>
        {
            preference.ToTable("user_preference");
            preference.HasKey(p => new { p.UserId, p.OrganizationId });
            preference.Property(p => p.Locale).HasMaxLength(10);
        });

        modelBuilder.Entity<PendingEvent>(pending =>
        {
            pending.ToTable("pending_event");
            pending.HasKey(p => new { p.OrganizationId, p.EventId });
            pending.Property(p => p.Kind).HasMaxLength(40);
            pending.Property(p => p.LastError).HasMaxLength(2000);
            pending.HasIndex(p => new { p.OrganizationId, p.At }).HasDatabaseName("ix_pending_event_org");
        });

        modelBuilder.Entity<PushSubscription>(subscription =>
        {
            subscription.ToTable("push_subscription");
            subscription.HasKey(s => s.Id);
            subscription.Property(s => s.Id).HasDefaultValueSql("uuidv7()");
            subscription.Property(s => s.Endpoint).HasMaxLength(2048);
            subscription.Property(s => s.EndpointHash).HasMaxLength(64);
            subscription.Property(s => s.P256dh).HasMaxLength(100);
            subscription.Property(s => s.Auth).HasMaxLength(50);
            subscription.Property(s => s.UserAgent).HasMaxLength(500);
            subscription.Property(s => s.Label).HasMaxLength(100);
            // One row per user and device; the address is long, so the index holds its hash (master plan 4.7).
            subscription.HasIndex(s => new { s.UserId, s.EndpointHash }).IsUnique().HasDatabaseName("ux_push_subscription");
        });

        ApplyTenantFilters(modelBuilder);
    }
}

internal sealed class NotifyDesignTimeFactory : IDesignTimeDbContextFactory<NotifyDbContext>
{
    public NotifyDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotifyDbContext>().UseArdeaNpgsql("Host=localhost;Database=ardea", NotifyDbContext.Schema).Options, new TenantContext());
}
