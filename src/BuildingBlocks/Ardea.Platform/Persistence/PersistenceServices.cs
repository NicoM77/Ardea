using Ardea.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Persistence;

public static class PersistenceServices
{
    /// <summary>Unit of work and tenant context, one per request or job scope.</summary>
    public static IServiceCollection AddUnitOfWork(this IServiceCollection services)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<UnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<UnitOfWork>());
        services.AddScoped<IUnitOfWorkFactory, UnitOfWorkFactory>();
        services.AddModuleDbContext<PlatformDbContext>(PlatformDbContext.Schema);
        services.AddScoped<Audit.AuditOrigin>();
        services.AddScoped<Audit.IAuditLog, Audit.AuditLog>();
        services.AddScoped<Status.IInstanceStatusContributor, Status.PlatformStatusContributor>();
        services.AddScoped<Settings.IInstanceSettings, Settings.InstanceSettings>();
        services.AddScoped<Email.IEmailSender, Email.SmtpEmailSender>();
        return services;
    }

    /// <summary>Registers a module context bound to the scope's unit of work.</summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            var unitOfWork = sp.GetRequiredService<UnitOfWork>();
            options.UseArdeaNpgsql(unitOfWork.Connection, schema).AddInterceptors(new UnitOfWorkInterceptor(unitOfWork));
        });
        return services;
    }
}
