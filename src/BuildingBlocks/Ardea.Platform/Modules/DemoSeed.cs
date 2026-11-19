using Ardea.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Modules;

/// <summary>
/// One module's part of <c>ardea seed-demo</c> (task 7.6): the host runs the steps of all modules in
/// <see cref="Order"/> and lets the job queue run dry after each, so the sample data passes through the real
/// pipeline. A step opens a scope per unit of work through <see cref="DemoSeedContext.CreateScope"/> and commits it.
/// </summary>
public interface IDemoSeedStep
{
    /// <summary>Steps run in ascending order; see <see cref="DemoSeedOrder"/>.</summary>
    int Order { get; }

    string Description { get; }

    Task SeedAsync(DemoSeedContext context, CancellationToken cancellationToken);
}

/// <summary>The order of the steps: the people first, then what they own, then what is known about the world.</summary>
public static class DemoSeedOrder
{
    public const int Accounts = 10;
    public const int Profile = 20;
    public const int Intelligence = 30;
    public const int Workflow = 50;
}

/// <summary>What the steps share: the scopes to work in and the identifiers earlier steps created.</summary>
public sealed class DemoSeedContext(IServiceScopeFactory scopes, TimeProvider time, Action<string> report)
{
    public TimeProvider Time { get; } = time;

    /// <summary>The demo organisation; set by the step that creates it.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>The members of the demo organisation, by short name (<c>nina</c>, <c>marc</c>, <c>lea</c>).</summary>
    public Dictionary<string, DemoMember> Members { get; } = new(StringComparer.Ordinal);

    /// <summary>The sign-in the command prints once.</summary>
    public DemoCredentials? Credentials { get; set; }

    /// <summary>Writes a progress line for the operator.</summary>
    public void Report(string line) => report(line);

    /// <summary>A scope for one unit of work with its own tenant; the caller commits it.</summary>
    public AsyncServiceScope CreateScope(Guid? organizationId = null, Guid? userId = null)
    {
        var scope = scopes.CreateAsyncScope();
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
