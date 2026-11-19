using System.Diagnostics;
using Ardea.Platform.Audit;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Npgsql;

namespace Ardea.Host.Commands;

/// <summary>
/// <c>ardea seed-demo</c> (task 7.6): fills an empty instance with the sample data of the mockups so that a visitor can
/// look around: an organisation with three people and three projects, sixteen real CVE records of the spike week,
/// and everything the pipeline makes of them (entries, findings, actions, history, notifications). The records go
/// through the real jobs; nothing is fetched and no GitHub token is needed. A database that holds data is refused
/// before anything is written. The owner's sign-in is printed once.
/// </summary>
internal static class SeedDemoCommand
{
    /// <summary>What counts as data: the tables a used instance fills first, with the word the refusal uses for them.</summary>
    private static readonly (string Table, string Noun)[] Occupancy =
    [
        ("iam.\"user\"", "account(s)"),
        ("iam.organization", "organisation(s)"),
        ("assets.project", "project(s)"),
        ("intel.source_record", "source record(s)"),
        ("intel.security_entry", "security entry/entries"),
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0)
        {
            await Console.Error.WriteLineAsync("Usage: ardea seed-demo (no options; the instance must be empty)");
            return 64;
        }

        var configuration = HostConfiguration.Load();
        var check = HostConfiguration.CheckMigration(configuration);
        if (check.IsValid)
        {
            check = HostConfiguration.CheckApplication(configuration);
        }

        if (!check.IsValid)
        {
            return HostConfiguration.Fail(check);
        }

        await using var provider = CommandServices.Build(configuration, "seed-demo", LogLevel.Warning);
        var behind = await SchemaGate.FindBehindAsync(provider.GetRequiredService<NpgsqlDataSource>(), provider.GetRequiredService<DatabaseOptions>(), CancellationToken.None);
        if (behind.Count > 0)
        {
            await Console.Error.WriteLineAsync($"The database schema is older than this version ({string.Join(", ", behind)}). Run `ardea migrate` first.");
            return 75;
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        return await SeedAsync(provider, Console.Out, Console.Error, cancellation.Token);
    }

    /// <summary>Seeds the instance <paramref name="services"/> point to; 0 when done, 1 when it holds data or a step failed.</summary>
    public static async Task<int> SeedAsync(IServiceProvider services, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var database = services.GetRequiredService<DatabaseOptions>();
        await using var owner = new NpgsqlConnection(database.OwnerConnectionString("ardea-seed-demo"));
        await owner.OpenAsync(cancellationToken);

        // Two runs at once would both see an empty instance.
        await using (var guard = new NpgsqlCommand("SELECT pg_advisory_lock(hashtext('ardea-seed-demo'))", owner))
        {
            await guard.ExecuteNonQueryAsync(cancellationToken);
        }

        if (await HoldsAsync(owner, cancellationToken) is { Length: > 0 } holds)
        {
            await error.WriteLineAsync($"The database holds data ({holds}). seed-demo works only on an empty instance and changed nothing.");
            return 1;
        }

        var time = services.GetRequiredService<TimeProvider>();
        var context = new DemoSeedContext(services.GetRequiredService<IServiceScopeFactory>(), time, line => output.WriteLine($"  {line}"));
        try
        {
            foreach (var step in services.GetServices<IDemoSeedStep>().OrderBy(s => s.Order))
            {
                output.WriteLine($"Seeding {step.Description} ...");
                await step.SeedAsync(context, cancellationToken);
                var jobs = await DemoJobRunner.RunUntilIdleAsync(services, cancellationToken);
                if (jobs > 0)
                {
                    output.WriteLine($"  {jobs} jobs run");
                }
            }

            await RecordAsync(services, context, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await error.WriteLineAsync($"seed-demo failed: {ex.Message}");
            await error.WriteLineAsync("The instance is partly filled and a second run refuses it. Start over with an empty database (with Compose: `docker compose down -v`).");
            return 1;
        }

        await WriteSummaryAsync(owner, context, watch.Elapsed, output, cancellationToken);
        return 0;
    }

    /// <summary>The tables that hold rows, as «2 account(s), 1 organisation(s)»; empty when the instance is empty. Read as the owner, so row-level security hides nothing.</summary>
    private static async Task<string> HoldsAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        foreach (var (table, noun) in Occupancy)
        {
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", owner);
            if ((long)(await command.ExecuteScalarAsync(cancellationToken))! is > 0 and var count)
            {
                found.Add($"{count} {noun}");
            }
        }

        return string.Join(", ", found);
    }

    private static async Task RecordAsync(IServiceProvider services, DemoSeedContext context, CancellationToken cancellationToken)
    {
        await using var scope = context.CreateScope();
        scope.ServiceProvider.GetRequiredService<AuditOrigin>().Origin = "command:seed-demo";
        await scope.ServiceProvider.GetRequiredService<IAuditLog>().RecordAsync(
            new AuditEntry("instance.demo_seeded", "organization", context.OrganizationId.ToString(), new { slug = "example-digital" }) { InstanceEvent = true },
            cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
    }

    private static async Task WriteSummaryAsync(NpgsqlConnection owner, DemoSeedContext context, TimeSpan elapsed, TextWriter output, CancellationToken cancellationToken)
    {
        async Task<long> CountAsync(string query)
        {
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM {query}", owner);
            return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        }

        output.WriteLine();
        output.WriteLine($"Demo instance ready in {elapsed.TotalSeconds:F0} s: {await CountAsync("iam.organization")} organisation, {await CountAsync("assets.project")} projects, "
            + $"{await CountAsync("intel.security_entry")} entries, {await CountAsync("assess.finding WHERE level >= 2")} relevant findings, "
            + $"{await CountAsync("assess.remediation WHERE status <> 'closed'")} open and {await CountAsync("assess.remediation WHERE status = 'closed'")} done actions, "
            + $"{await CountAsync("notify.notification")} notifications in the app.");
        if (context.Credentials is { } credentials)
        {
            output.WriteLine();
            output.WriteLine("Sign in with the demo owner (shown once, stored only as a hash):");
            output.WriteLine($"  e-mail    {credentials.Email}");
            output.WriteLine($"  password  {credentials.Password}");
            output.WriteLine("`ardea admin reset-password` sets a new one. The sources are switched off: the demo fetches nothing until an administrator turns them on.");
        }
    }
}
