using Ardea.Assessment.Persistence;
using Ardea.Assets.Contracts;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assessment.Features;

/// <summary>
/// The work of the demo's people on the actions and notices the pipeline raised (<c>ardea seed-demo</c>, tasks 7.6 and 5a.3),
/// done through the commands the endpoints use: notes on two open actions, one action closed as not affected, and two of the
/// stack's notices answered. Actions stay open or
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

        // The stack's notices (task 5a.3): one «doesn't affect us», one «affects us» on the stack entry; the rest stay new.
        var notices = await OpenNoticesAsync(context, cancellationToken);
        var answered = 0;
        if (notices.TryGetValue("CVE-2026-116304", out var postgresNotice))
        {
            await ActAsync<NoticeCommands>(context, "lea", async (commands, user) => Check(await commands.AnswerAsync(
                postgresNotice, new AnswerNoticeRequest(Notice.NotAffected, Notice.Open), user, cancellationToken)), cancellationToken);
            answered++;
        }

        if (notices.TryGetValue("CVE-2026-89788", out var kernelNotice))
        {
            await ActAsync<NoticeCommands>(context, "marc", async (commands, user) => Check(await commands.AnswerAsync(
                kernelNotice, new AnswerNoticeRequest(Notice.AffectsUs, Notice.Open), user, cancellationToken)), cancellationToken);
            answered++;
        }

        context.Report($"{done} steps of work on {numbers.Count} actions; {answered} of {notices.Count} notices answered");
    }

    /// <summary>The open notices by their entry's key.</summary>
    private static async Task<Dictionary<string, Guid>> OpenNoticesAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        await using var scope = context.CreateScope(context.OrganizationId, context.Members["nina"].UserId);
        return await scope.ServiceProvider.GetRequiredService<AssessDbContext>().Notices.AsNoTracking()
            .Where(n => n.State == Notice.Open).ToDictionaryAsync(n => n.EntryKey, n => n.Id, StringComparer.Ordinal, cancellationToken);
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
    private static Task ActAsync(DemoSeedContext context, string person, Func<ActionCommands, Guid, Task> work, CancellationToken cancellationToken) =>
        ActAsync<ActionCommands>(context, person, work, cancellationToken);

    private static async Task ActAsync<TCommands>(DemoSeedContext context, string person, Func<TCommands, Guid, Task> work, CancellationToken cancellationToken)
        where TCommands : notnull
    {
        var user = context.Members[person].UserId;
        await using var scope = context.CreateScope(context.OrganizationId, user);
        var services = scope.ServiceProvider;
        await work(services.GetRequiredService<TCommands>(), user);
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
