using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Ardea.Platform.Persistence;

public interface IOrganizationOwned
{
    Guid OrganizationId { get; set; }
}

public abstract class ModuleDbContext : DbContext
{
    public const string TenantFilter = "tenant";

    private readonly ITenantContext? _tenant;

    protected ModuleDbContext(DbContextOptions options, ITenantContext? tenant)
        : base(options)
    {
        _tenant = tenant;
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
    }

    protected Guid CurrentOrganizationId => _tenant?.OrganizationId ?? throw new MissingTenantContextException();

    protected void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(e => typeof(IOrganizationOwned).IsAssignableFrom(e.ClrType)))
        {
            var method = typeof(ModuleDbContext).GetMethod(nameof(ApplyTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(entity.ClrType);
            method.Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder)
        where T : class, IOrganizationOwned
    {
        modelBuilder.Entity<T>().HasQueryFilter(TenantFilter, e => e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<T>().Property(e => e.OrganizationId).IsConcurrencyToken();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CheckTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CheckTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void CheckTenantWrites()
    {
        foreach (EntityEntry<IOrganizationOwned> entry in ChangeTracker.Entries<IOrganizationOwned>())
        {
            if (entry.State == EntityState.Unchanged || entry.State == EntityState.Detached)
            {
                continue;
            }

            var current = CurrentOrganizationId;
            if (entry.State == EntityState.Added && entry.Entity.OrganizationId == Guid.Empty)
            {
                entry.Entity.OrganizationId = current;
            }

            var original = entry.State == EntityState.Added
                ? entry.Entity.OrganizationId
                : entry.Property(e => e.OrganizationId).OriginalValue;
            if (entry.Entity.OrganizationId != current || original != current)
            {
                throw new InvalidOperationException(
                    $"{entry.Metadata.ClrType.Name} belongs to another organisation than the unit of work.");
            }
        }
    }
}
