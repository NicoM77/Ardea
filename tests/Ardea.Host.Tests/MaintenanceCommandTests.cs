using Ardea.Host.Commands;
using Microsoft.Extensions.Configuration;

namespace Ardea.Host.Tests;

public sealed class MaintenanceCommandTests
{
    [Fact]
    public void Every_module_command_is_called_as_the_master_plan_names_it()
    {
        var commands = Modules.All.SelectMany(m => m.Commands).ToList();

        Assert.Equal(
            ["assess-recompute", "catalog-grow", "intel-import", "intel-recompute", "intel-renormalize", "reset-password"],
            commands.Select(c => c.Name).Order(StringComparer.Ordinal));
        foreach (var command in commands.Where(c => c.Name.Contains('-', StringComparison.Ordinal) && c.Name != "reset-password"))
        {
            var group = command.Name[..command.Name.IndexOf('-', StringComparison.Ordinal)];
            Assert.Contains(group, AdminCommand.GroupPrefixes);
            Assert.StartsWith(group + " ", command.Usage, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Help_lists_every_command()
    {
        var writer = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(writer);
        try
        {
            HelpCommand.Run("help");
        }
        finally
        {
            Console.SetOut(previous);
        }

        var help = writer.ToString();
        foreach (var expected in new[] { "doctor", "admin <command>", "intel renormalize", "intel recompute", "intel import --full", "assess recompute", "catalog grow", "org export", "org delete" })
        {
            Assert.Contains(expected, help, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Doctor_reports_unreachable_services_and_a_wrong_provider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SMTP:HOST"] = "mail.example.org",
            ["AI:PROVIDERS:local:KIND"] = "ollama",
            ["AI:PROVIDERS:local:ENDPOINT"] = "http://ollama:11434",
            ["AI:PROVIDERS:local:MODEL"] = "qwen3:14b",
        }).Build();
        var sources = new Dictionary<string, IReadOnlyList<string>> { ["osv"] = ["api.osv.dev"], ["kev"] = ["www.cisa.gov", "down.example.org"] };
        Task<string?> Probe(string host, int port, CancellationToken ct) => Task.FromResult<string?>(host is "ollama" or "down.example.org" ? "ConnectionRefused" : null);

        var results = await DoctorServiceChecks.RunAsync(configuration, Probe, sources, CancellationToken.None);

        Assert.Equal(
            [
                "ai local Warn ollama ollama:11434 not reachable: ConnectionRefused",
                "smtp Ok mail.example.org:587 reachable",
                "source kev Warn not reachable: down.example.org (ConnectionRefused)",
                "source osv Ok 1 host(s) reachable",
            ],
            results.Select(r => $"{r.Check} {r.Level} {r.Detail}").Order(StringComparer.Ordinal));

        var wrong = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AI:PROVIDERS:x:KIND"] = "openai" }).Build();
        var failed = await DoctorServiceChecks.RunAsync(wrong, Probe, new Dictionary<string, IReadOnlyList<string>>(), CancellationToken.None);
        Assert.Contains(failed, r => r is { Check: "ai", Level: DoctorLevel.Fail });
        Assert.Contains(failed, r => r is { Check: "smtp", Level: DoctorLevel.Ok });
    }
}
