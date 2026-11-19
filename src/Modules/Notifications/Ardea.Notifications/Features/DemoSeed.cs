using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Notifications.Features;

/// <summary>
/// The last step of <c>ardea seed-demo</c> (task 7.6): the messages the demo's events would send outside the app, by
/// e-mail on an instance with SMTP, are suppressed before a worker can send them. The demo's people have invented
/// addresses, and their notifications in the app are the ones to look at. The delivery log says why.
/// </summary>
internal sealed class DemoSeed : IDemoSeedStep
{
    internal const string Reason = "Not sent: a message about the sample data of seed-demo, whose people have invented addresses.";

    public int Order => DemoSeedOrder.Quiet;

    public string Description => "no messages outside the app";

    public async Task SeedAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        await using var scope = context.CreateScope(context.OrganizationId);
        var services = scope.ServiceProvider;
        var suppressed = await services.GetRequiredService<NotifyDbContext>().Deliveries
            .Where(d => d.Channel != Channels.InApp && (d.State == "pending" || d.State == "failed"))
            .ExecuteUpdateAsync(d => d.SetProperty(x => x.State, "suppressed").SetProperty(x => x.LastError, Reason), cancellationToken);
        await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        if (suppressed > 0)
        {
            context.Report($"{suppressed} message(s) outside the app suppressed");
        }
    }
}
