using Ardea.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Modules;

public interface IDemoSeedStep
{
    int Order { get; }

    string Description { get; }

    Task SeedAsync(DemoSeedContext context, CancellationToken cancellationToken);
}

public static class DemoSeedOrder
{
    public const int Accounts = 10;
    public const int Profile = 20;
    public const int Intelligence = 30;
    public const int Workflow = 50;
    public const int Updates = 60;
    public const int Quiet = 90;
}

public sealed class DemoSeedContext(IServiceScopeFactory scopes, TimeProvider time, Action<string> report)
{
    public TimeProvider Time { get; } = time;

    public Guid OrganizationId { get; set; }

    public Dictionary<string, DemoMember> Members { get; } = new(StringComparer.Ordinal);

    public DemoCredentials? Credentials { get; set; }

    public void Report(string line) => report(line);

    public const string AuditOrigin = "command:seed-demo";

    public AsyncServiceScope CreateScope(Guid? organizationId = null, Guid? userId = null)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<Audit.AuditOrigin>().Origin = AuditOrigin;
        var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
        if (organizationId is { } organization)
        {
            tenant.SetOrganization(organization);
        }

        if (userId is { } user)
        {
            tenant.SetUser(user);
        }

        return scope;
    }
}

public sealed record DemoMember(Guid UserId, string DisplayName, string Email, string Role);

public sealed record DemoCredentials(string Email, string Password);
