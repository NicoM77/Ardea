using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assessment.Features;

/// <summary>
/// The work of the demo's people on the actions the pipeline raised (<c>ardea seed-demo</c>, task 7.6), done through the
/// commands the endpoints use: notes on two open actions and one action closed as not affected. Actions stay open or
/// done; nobody is assigned, no deadline is set by hand and nothing is put in review, because phase 5a removes these.
/// The history and the notifications the steps cause are the real ones. An action the rules did not raise is skipped.
/// </summary>
internal sealed class DemoSeed : IDemoSeedStep
{
    public int Order => DemoSeedOrder.Workflow;

    public string Description => "work on the actions";

    public async Task SeedAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        var numbers = await ActionNumbersAsync(context, cancellationToken);
        var done = 0;

        if (numbers.TryGetValue("php", out var php))
        {
            await ActAsync(context, "marc", async (commands, user) => Check(await commands.AddNoteAsync(
                php, new AddNoteRequest("PHP 8.4.27 image is building; rollout to the webshop in tonight's maintenance window."), user, cancellationToken)), cancellationToken);
            done++;
        }

        if (numbers.TryGetValue("postgresql", out var postgres))
        {
            await ActAsync(context, "lea", async (commands, user) => Check(await commands.AddNoteAsync(
                postgres, new AddNoteRequest("We use reindexdb in the nightly maintenance and PL/Perl in one reporting function; planning 17.12 for the next window."), user, cancellationToken)), cancellationToken);
            done++;
        }

        if (numbers.TryGetValue("linux-kernel", out var kernel))
        {
            await ActAsync(context, "marc", async (commands, user) => Check(await commands.SetActionStatusAsync(
                kernel, new SetActionStatusRequest("not_affected", "open", "Our node kernels are built without ksmbd (CONFIG_SMB_SERVER) and Xen netback (CONFIG_XEN_NETDEV_BACKEND)."), user, cancellationToken)), cancellationToken);
            done++;
        }

        context.Report($"{done} steps of work on {numbers.Count} actions");
    }

    /// <summary>The numbers of the open actions by the component's product key; the first where several components share one.</summary>
    private static async Task<Dictionary<string, int>> ActionNumbersAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        await using var scope = context.CreateScope(context.OrganizationId, context.Members["nina"].UserId);
        var actions = await scope.ServiceProvider.GetRequiredService<AssessDbContext>().Remediations.AsNoTracking()
            .Where(r => r.Status == "open").OrderBy(r => r.Number).Select(r => new { r.Number, r.ComponentId }).ToListAsync(cancellationToken);
        var components = await scope.ServiceProvider.GetRequiredService<IProfileQueries>()
            .FindComponentsAsync([.. actions.Select(a => a.ComponentId)], cancellationToken);
        var byComponent = components.Where(c => c.ProductKey is not null).ToDictionary(c => c.Id, c => c.ProductKey!);
        return actions.Where(a => byComponent.ContainsKey(a.ComponentId))
            .GroupBy(a => byComponent[a.ComponentId], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Number, StringComparer.Ordinal);
    }

    /// <summary>One unit of work as one person, committed when the work is done.</summary>
    private static async Task ActAsync(DemoSeedContext context, string person, Func<ActionCommands, Guid, Task> work, CancellationToken cancellationToken)
    {
        var user = context.Members[person].UserId;
        await using var scope = context.CreateScope(context.OrganizationId, user);
        var services = scope.ServiceProvider;
        await work(services.GetRequiredService<ActionCommands>(), user);
        await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
    }

    private static void Check<T>(CommandResult<T> result)
    {
        if (result.Refusal is { } refusal)
        {
            throw new InvalidOperationException($"{refusal.Title} ({refusal.Code})");
        }
    }
}
