using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Audit;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Notifications.Features;

/// <summary>
/// The last step of <c>ardea seed-demo</c> (task 7.6): the demo organisation keeps its notifications in the app only.
/// Its people have invented addresses, so nothing may leave the instance for them, now or later: the e-mail and push
/// targets of the default rules are removed, a rule left without another target (the weekly summary) is switched off
/// with its target kept, the digest items noted for those targets are dropped, and the messages already queued for
/// them are suppressed with a reason in the delivery log. An administrator who adds real people turns them on again.
/// </summary>
internal sealed class DemoSeed : IDemoSeedStep
{
    internal const string Reason = "Not sent: a message about the sample data of seed-demo, whose people have invented addresses.";

    public int Order => DemoSeedOrder.Quiet;

    public string Description => "notifications in the app only";

    public async Task SeedAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        await using var scope = context.CreateScope(context.OrganizationId);
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<NotifyDbContext>();
        var audit = services.GetRequiredService<IAuditLog>();
        var now = context.Time.GetUtcNow();

        var rules = await db.Rules.Include(r => r.Targets).ToListAsync(cancellationToken);
        var outside = rules.SelectMany(r => r.Targets).Where(t => t.Channel != Channels.InApp).Select(t => t.Id).ToList();
        await db.DigestItems.Where(i => outside.Contains(i.RuleTargetId)).ExecuteDeleteAsync(cancellationToken);
        foreach (var rule in rules.Where(r => r.Targets.Exists(t => t.Channel != Channels.InApp)))
        {
            if (rule.Targets.TrueForAll(t => t.Channel != Channels.InApp))
            {
                rule.Enabled = false;
            }
            else
            {
                db.Targets.RemoveRange(rule.Targets.Where(t => t.Channel != Channels.InApp));
            }

            rule.ChangedAt = now;
            await audit.RecordAsync(new AuditEntry("notify.rule_changed", "rule", rule.Id.ToString(), new { rule.Name, rule.Enabled, reason = "seed-demo: in the app only" }), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        var suppressed = await db.Deliveries
            .Where(d => d.Channel != Channels.InApp && (d.State == "pending" || d.State == "failed"))
            .ExecuteUpdateAsync(d => d.SetProperty(x => x.State, "suppressed").SetProperty(x => x.LastError, Reason), cancellationToken);
        await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        context.Report($"{outside.Count} targets outside the app taken off, {suppressed} queued message(s) suppressed");
    }
}
