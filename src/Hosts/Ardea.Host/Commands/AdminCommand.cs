
namespace Ardea.Host.Commands;

/// <summary><c>ardea admin &lt;command&gt;</c>: maintenance commands of the modules (master plan 12.8).</summary>
internal static class AdminCommand
{
    /// <summary>
    /// The groups whose commands are also called without <c>admin</c>: <c>ardea intel recompute</c> runs the command
    /// named <c>intel-recompute</c>. A module command named <c>group-verb</c> needs its group here.
    /// </summary>
    public static readonly IReadOnlyList<string> GroupPrefixes = ["intel", "assess", "catalog"];

    public static async Task<int> RunAsync(string[] args)
    {
        var commands = Modules.All.SelectMany(m => m.Commands).ToDictionary(c => c.Name, StringComparer.Ordinal);
        if (args.Length == 0 || !commands.TryGetValue(args[0], out var command))
        {
            var output = args.Length == 0 ? Console.Out : Console.Error;
            output.WriteLine("Usage: ardea admin <command>\n\nCommands");
            foreach (var c in commands.Values.OrderBy(c => c.Name, StringComparer.Ordinal))
            {
                output.WriteLine($"  {c.Usage,-40} {c.Description}");
            }

            return args.Length == 0 ? 0 : 64;
        }

        var configuration = HostConfiguration.Load();
        var check = HostConfiguration.CheckApplication(configuration);
        if (!check.IsValid)
        {
            return HostConfiguration.Fail(check);
        }

        await using var provider = CommandServices.Build(configuration, "admin");
        await using var scope = provider.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        return await command.RunAsync(scope.ServiceProvider, args[1..], cancellation.Token);
    }
}
