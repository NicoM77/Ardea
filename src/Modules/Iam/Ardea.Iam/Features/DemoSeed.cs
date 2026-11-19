using Ardea.Iam.Domain;
using Ardea.Iam.Persistence;
using Ardea.Platform.Audit;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Iam.Features;

/// <summary>
/// The people of the demo (<c>ardea seed-demo</c>, task 7.6): the organisation of the mockups with its owner, an
/// administrator and a member. Only the owner's generated password is shown, once; the others cannot sign in until
/// an owner resets their passwords. Each person is one unit of work, because an account is visible only to itself.
/// </summary>
internal sealed class DemoSeed : IDemoSeedStep
{
    public const string OrganizationName = "Example Digital AG";
    public const string OrganizationSlug = "example-digital";

    private static readonly (string Key, string Name, string Email, string Role)[] People =
    [
        ("nina", "Nina Meier", "nina.meier@example.org", "owner"),
        ("marc", "Marc Keller", "marc.keller@example.org", "admin"),
        ("lea", "Lea Brunner", "lea.brunner@example.org", "member"),
    ];

    public int Order => DemoSeedOrder.Accounts;

    public string Description => "organisation and three members";

    public async Task SeedAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        context.OrganizationId = Guid.CreateVersion7();
        foreach (var (key, name, email, role) in People)
        {
            var password = ResetPasswordCommand.Generate();
            var user = await AddPersonAsync(context, name, email, role, password, cancellationToken);
            context.Members[key] = new DemoMember(user, name, email, role);
            if (role == "owner")
            {
                context.Credentials = new DemoCredentials(email, password);
            }
        }
    }

    private static async Task<Guid> AddPersonAsync(
        DemoSeedContext context, string name, string email, string role, string password, CancellationToken cancellationToken)
    {
        var userId = Guid.CreateVersion7();
        var organizationId = context.OrganizationId;
        await using var scope = context.CreateScope(organizationId, userId);
        var services = scope.ServiceProvider;
        var now = context.Time.GetUtcNow();
        var user = AuthEndpoints.NewUser(email, name, context.Time, userId);
        user.EmailConfirmed = true;
        user.IsInstanceAdmin = role == "owner";
        var created = await services.GetRequiredService<UserManager<ArdeaUser>>().CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException($"The demo account {email} could not be created: {string.Join(" ", created.Errors.Select(e => e.Description))}");
        }

        var db = services.GetRequiredService<IamDbContext>();
        if (role == "owner")
        {
            db.Organizations.Add(new Organization { OrganizationId = organizationId, Name = OrganizationName, Slug = OrganizationSlug, CreatedAt = now });
        }

        db.Memberships.Add(new Membership { Id = Guid.CreateVersion7(), OrganizationId = organizationId, UserId = userId, Role = role, CreatedAt = now });
        await db.SaveChangesAsync(cancellationToken);
        var audit = services.GetRequiredService<IAuditLog>();
        if (role == "owner")
        {
            await audit.RecordAsync(new AuditEntry("org.created", "organization", organizationId.ToString(), new { name = OrganizationName, slug = OrganizationSlug }), cancellationToken);
            foreach (var subscriber in services.GetServices<Contracts.IOrganizationCreatedSubscriber>())
            {
                await subscriber.OnOrganizationCreatedAsync(organizationId, cancellationToken);
            }
        }

        await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        return userId;
    }
}
