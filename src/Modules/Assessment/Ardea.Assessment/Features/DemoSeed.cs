using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assessment.Features;

/// <summary>
/// The work of the demo's people on the actions the pipeline raised (<c>ardea seed-demo</c>, task 7.6), done through the
/// commands the endpoints use: one action in review with its assignee, one assigned, one closed, one with a note. The
/// history and the notifications they cause are the real ones. An action the rules did not raise is skipped.
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
            await ActAsync(context, "nina", async (commands, members, _) => Check(await commands.UpdateActionAsync(
                php, new UpdateActionRequest(context.Members["marc"].UserId, null, null, null), context.Members["nina"].UserId, members, cancellationToken)), cancellationToken);
            await ActAsync(context, "marc", async (commands, _, user) => Check(await commands.SetActionStatusAsync(
                php, new SetActionStatusRequest("in_review", "open", "Patched image is building; rollout in tonight's maintenance window."), user, cancellationToken)), cancellationToken);
            done += 2;
        }

        if (numbers.TryGetValue("umbraco", out var umbraco))
        {
            await ActAsync(context, "nina", async (commands, members, user) => Check(await commands.UpdateActionAsync(
                umbraco, new UpdateActionRequest(user, null, null, null), user, members, cancellationToken)), cancellationToken);
            done++;
        }

        if (numbers.TryGetValue("postgresql", out var postgres))
        {
            await ActAsync(context, "lea", async (commands, _, user) => Check(await commands.AddNoteAsync(
                postgres, new AddNoteRequest("Checking which of the PL languages and pg_restore we use before planning the update."), user, cancellationToken)), cancellationToken);
            done++;
        }

        if (numbers.TryGetValue("dotnet", out var dotnet))
        {
            await ActAsync(context, "nina", async (commands, _, user) => Check(await commands.SetActionStatusAsync(
                dotnet, new SetActionStatusRequest("remediated", "open", "Base image updated to the latest 10.0 patch release."), user, cancellationToken)), cancellationToken);
            done++;
        }

        context.Report($"{done} steps of work on {numbers.Count} actions");
    }

    /// <summary>The numbers of the open actions by the component's product key.</summary>
    private static async Task<Dictionary<string, int>> ActionNumbersAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        await using var scope = context.CreateScope(context.OrganizationId, context.Members["nina"].UserId);
        var actions = await scope.ServiceProvider.GetRequiredService<AssessDbContext>().Remediations.AsNoTracking()
            .Where(r => r.Status == "open").Select(r => new { r.Number, r.ComponentId }).ToListAsync(cancellationToken);
        var components = await scope.ServiceProvider.GetRequiredService<IProfileQueries>()
            .FindComponentsAsync([.. actions.Select(a => a.ComponentId)], cancellationToken);
        var byComponent = components.Where(c => c.ProductKey is not null).ToDictionary(c => c.Id, c => c.ProductKey!);
        return actions.Where(a => byComponent.ContainsKey(a.ComponentId)).ToDictionary(a => byComponent[a.ComponentId], a => a.Number, StringComparer.Ordinal);
    }

    /// <summary>One unit of work as one person, committed when the work is done.</summary>
    private static async Task ActAsync(
        DemoSeedContext context, string person, Func<ActionCommands, IOrganizationMembership, Guid, Task> work, CancellationToken cancellationToken)
    {
        var user = context.Members[person].UserId;
        await using var scope = context.CreateScope(context.OrganizationId, user);
        var services = scope.ServiceProvider;
        await work(services.GetRequiredService<ActionCommands>(), services.GetRequiredService<IOrganizationMembership>(), user);
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
