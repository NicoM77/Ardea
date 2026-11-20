using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Ardea.Assessment.Matching;
using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Sources.Abstractions;
using Ardea.TestSupport;

namespace Ardea.Assessment.Tests.Reference;

/// <summary>
/// The notice spike of task 5a.1 (master plan 17.9, ADR 044), run on the real generation of task 5a.3 (ADR 047): how many
/// notices a typical organisation stack gets in one week. Every record of the spike week (11 to 17 November 2026, newest
/// state each) goes through the real pipeline (CVE Program parser, correlation, forward search, candidates, version check,
/// notices) against one organisation per stack, whose stack entries carry no version or a line and the default threshold
/// «only important». The notices are read from <c>assess.notice</c>; the other columns count the hits from the findings,
/// to show what the thresholds of ADR 044 leave out.
/// <para>
/// Opt-in, never in CI (master plan 13.6): the full week is not in the repository. Unpack the CVE Program's delta
/// archives of the week into one folder per day (<c>11</c> … <c>17</c>) and run
/// <c>ARDEA_SPIKE_DIR=&lt;folder&gt; dotnet test --project tests/Ardea.Assessment.Tests --filter-class "*NoticeSpike"</c>.
/// The report lands next to the test binaries as <c>notice-spike-report.md</c>. CI checks the rules on the week's subset in
/// the repository (<see cref="ReferenceSetTests"/>) and on small cases (<see cref="NoticeTests"/>).
/// </para>
/// </summary>
public sealed class NoticeSpike(PostgresServer server)
{
    /// <summary>The acceptance of phase 5a (ADR 044): a stack of ten entries gets at most this many notices in the spike week.</summary>
    internal const int MaxNoticesPerWeek = 25;

    /// <summary>Records published from this day on are new in the week; older ones changed in it (the 10th was a Patch Tuesday).</summary>
    private static readonly DateTimeOffset WeekStart = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Thresholds for the what-if columns; each lets an actively exploited entry pass.</summary>
    internal enum Threshold
    {
        /// <summary>Every hit: «everything».</summary>
        Everything,

        /// <summary>CVSS 4.0 or more.</summary>
        Medium,

        /// <summary>CVSS 7.0 or more: «only important» as master plan 17.2 first proposed it.</summary>
        High,

        /// <summary>CVSS 9.0 or more: «only important» as ADR 044 decides it.</summary>
        Critical,

        /// <summary>Actively exploited only: «only important» for products the catalogue marks as noisy (ADR 044).</summary>
        Exploited,
    }

    /// <summary>A stack entry: one technology, as one or more catalogue products, optionally a version line.</summary>
    /// <param name="Label">How the organisation names it.</param>
    /// <param name="Products">Catalogue keys; a key starting with <c>name:</c> is a free entry «vendor/name» the catalogue lacks.</param>
    /// <param name="Line">A version line such as <c>17</c> or <c>8.3</c>, stored with the precision <c>line</c>; <c>null</c> for none.</param>
    /// <param name="Default">The entry's «only important» as ADR 044 measured it, to compare with the real generation.</param>
    internal sealed record StackEntry(string Label, string[] Products, string? Line = null, Threshold Default = Threshold.Critical);

    internal sealed record Stack(string Name, StackEntry[] Entries);

    /// <summary>Typical stacks from master plan 17.8, ten entries each, and the spike's three example projects without versions.</summary>
    internal static readonly Stack[] Stacks =
    [
        new("Web agency: .NET, Angular, Azure",
        [
            new(".NET / ASP.NET Core", ["dotnet", "aspnetcore"]),
            new("Angular", ["angular"]),
            new("Node.js 22", ["nodejs"], "22"),
            new("Umbraco", ["umbraco"]),
            new("Microsoft SQL Server", ["sql-server"]),
            new("PostgreSQL 17", ["postgresql"], "17"),
            new("Redis", ["redis"]),
            new("IIS", ["iis"]),
            new("Windows Server", ["windows-server"]),
            new("Microsoft Azure", ["name:Microsoft/Azure"]),
        ]),
        new("Hoster: PHP, WordPress, TYPO3 on Linux",
        [
            new("PHP 8.3", ["php"], "8.3"),
            new("WordPress", ["wordpress"]),
            new("TYPO3", ["typo3"]),
            new("Shopware", ["shopware"]),
            new("MySQL 8.0", ["mysql"], "8.0"),
            new("nginx", ["nginx"]),
            new("Apache HTTP Server", ["apache-httpd"]),
            new("Ubuntu Linux", ["ubuntu"]),
            new("Linux kernel", ["linux-kernel"]),
            new("Docker", ["docker-engine"]),
        ]),
        new("Enterprise IT: Windows, Exchange, FortiGate, VMware",
        [
            new("Windows Server", ["windows-server"]),
            new("Windows 11", ["windows"]),
            new("Microsoft Exchange Server", ["exchange-server"]),
            new("Microsoft SQL Server", ["sql-server"]),
            new("Microsoft Office", ["office"]),
            new("Fortinet FortiGate", ["fortios"]),
            new("VMware vSphere", ["esxi", "vcenter-server"]),
            new("Veeam Backup & Replication", ["veeam-backup"]),
            new("Ubuntu Linux", ["ubuntu"]),
            new("Google Chrome", ["chrome"], Default: Threshold.Exploited),
        ]),
        new("SaaS: Java, React, Kubernetes",
        [
            new("Java 21", ["openjdk"], "21"),
            new("Spring", ["spring-framework", "spring-boot"]),
            new("React", ["react"]),
            new("Node.js", ["nodejs"]),
            new("PostgreSQL", ["postgresql"]),
            new("MongoDB", ["mongodb"]),
            new("RabbitMQ", ["rabbitmq"]),
            new("Elasticsearch", ["elasticsearch"]),
            new("Kubernetes", ["kubernetes"]),
            new("Linux kernel", ["linux-kernel"]),
        ]),
        new("Spike A: .NET, Angular, Umbraco, Azure SQL",
        [
            new(".NET", ["dotnet"]),
            new("Angular", ["angular"]),
            new("Umbraco", ["umbraco"]),
            new("Azure SQL Database", ["name:Azure SQL Database"]),
        ]),
        new("Spike B: WordPress, PHP, MySQL",
        [
            new("WordPress", ["wordpress"]),
            new("PHP", ["php"]),
            new("MySQL", ["mysql"]),
        ]),
        new("Spike C: Kubernetes, Linux, Redis, PostgreSQL",
        [
            new("Kubernetes", ["kubernetes"]),
            new("Linux kernel", ["linux-kernel"]),
            new("Redis", ["redis"]),
            new("PostgreSQL", ["postgresql"]),
        ]),
    ];

    [Fact(Timeout = 3_600_000)]
    public async Task Notices_per_week_for_typical_stacks()
    {
        var folder = Environment.GetEnvironmentVariable("ARDEA_SPIKE_DIR");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(folder), "Set ARDEA_SPIKE_DIR to the unpacked spike week (task 5a.1, ADR 044).");

        var watch = Stopwatch.StartNew();
        var records = WeekRecords(folder!);
        await using var host = await ReferenceHost.StartAsync(server);

        var components = new Dictionary<Guid, (Stack Stack, StackEntry Entry)>();
        foreach (var stack in Stacks)
        {
            var organization = Guid.CreateVersion7();
            var project = await host.AddProjectAsync(organization, stack.Name, new ReferenceProject("normal", "internet"), Project.StackKind);
            foreach (var entry in stack.Entries)
            {
                foreach (var product in entry.Products)
                {
                    var id = await host.AddComponentAsync(organization, project, c => Describe(c, product, entry.Line));
                    components[id] = (stack, entry);
                }
            }

            // The organisation was set up before the week: its first fill is done when the week's records arrive.
            await host.ProfileChangedAsync(organization, project);
        }

        await host.RunJobsAsync();
        foreach (var chunk in records.Chunk(500))
        {
            await host.StoreRecordsAsync(chunk);
        }

        await host.RunJobsAsync();

        var hits = (await host.RowsAsync("""
            SELECT f.component_id, e.primary_key, e.published_at, e.cvss_score, e.exploitation, f.match_type, f.evidence::text
            FROM assess.finding f JOIN intel.security_entry e ON e.id = f.entry_id
            """)).Select(row => Hit.Parse(row, components)).ToList();
        var notices = (await host.RowsAsync("""
            SELECT n.entry_key, c.id, n.state, n.is_backfill, e.published_at
            FROM assess.notice n CROSS JOIN LATERAL unnest(n.component_ids) AS c(id) JOIN intel.security_entry e ON e.id = n.entry_id
            """)).Select(row => Raised.Parse(row, components)).ToList();
        var entries = await host.RowsAsync("SELECT state, count(*) FROM intel.security_entry GROUP BY state ORDER BY state");

        // The backfill (ADR 047): the same stacks, set up after the week, get its notices from the reverse search at once.
        var late = new Dictionary<Guid, Stack>();
        foreach (var stack in Stacks.Where(s => s.Entries.Length == 10))
        {
            var organization = Guid.CreateVersion7();
            var project = await host.AddProjectAsync(organization, stack.Name, new ReferenceProject("normal", "internet"), Project.StackKind);
            foreach (var entry in stack.Entries)
            {
                foreach (var product in entry.Products)
                {
                    await host.AddComponentAsync(organization, project, c => Describe(c, product, entry.Line));
                }
            }

            await host.ProfileChangedAsync(organization, project);
            late[organization] = stack;
        }

        await host.RunJobsAsync();
        var backfilled = (await host.RowsAsync("""
            SELECT n.organization_id, count(*), count(*) FILTER (WHERE n.is_backfill AND n.state = 'open'),
                   (SELECT count(*) FROM assess.assess_event e WHERE e.organization_id = n.organization_id AND e.kind = 'notice.backfilled'),
                   (SELECT count(*) FROM assess.assess_event e WHERE e.organization_id = n.organization_id AND e.kind = 'notice.raised')
            FROM assess.notice n GROUP BY n.organization_id
            """)).Select(r => r.Split('\t')).Where(c => late.ContainsKey(Guid.Parse(c[0])))
            .ToDictionary(c => late[Guid.Parse(c[0])], c => (Notices: int.Parse(c[1], CultureInfo.InvariantCulture), Quiet: int.Parse(c[2], CultureInfo.InvariantCulture),
                Summaries: int.Parse(c[3], CultureInfo.InvariantCulture), Raised: int.Parse(c[4], CultureInfo.InvariantCulture)));

        var report = Report(records.Count, entries, hits, notices, watch.Elapsed);
        report += Environment.NewLine + "## Backfill: the ten-entry stacks set up after the week" + Environment.NewLine + Environment.NewLine
            + "| Stack | Notices | of them quiet (`is_backfill`, open) | `notice.backfilled` rows | `notice.raised` rows |" + Environment.NewLine
            + "|---|--:|--:|--:|--:|" + Environment.NewLine
            + string.Concat(late.Values.Select(s => backfilled.TryGetValue(s, out var b)
                ? $"| {s.Name} | {b.Notices} | {b.Quiet} | {b.Summaries} | {b.Raised} |{Environment.NewLine}"
                : $"| {s.Name} | 0 | 0 | 0 | 0 |{Environment.NewLine}"));
        var output = Path.Combine(AppContext.BaseDirectory, "notice-spike-report.md");
        await File.WriteAllTextAsync(output, report, TestContext.Current.CancellationToken);
        TestContext.Current.SendDiagnosticMessage($"Notice spike report: {output}");
        TestContext.Current.SendDiagnosticMessage(report);

        // Only open notices, raised by the forward search one by one: nothing was answered, nothing came from a first fill.
        Assert.All(notices, n => Assert.Equal(("open", false), (n.State, n.Backfill)));
        foreach (var stack in Stacks.Where(s => s.Entries.Length == 10))
        {
            var count = Generated(notices, stack).Count;
            Assert.True(count <= MaxNoticesPerWeek, $"{stack.Name}: {count} notices, at most {MaxNoticesPerWeek} (ADR 044).{Environment.NewLine}{report}");
        }

        // Set up after the week, a stack gets the same notices, all quiet, with one summary and no event each.
        foreach (var (stack, b) in backfilled)
        {
            Assert.Equal((Generated(notices, stack).Count, Generated(notices, stack).Count, 1, 0), (b.Notices, b.Quiet, b.Summaries, b.Raised));
        }
    }

    /// <summary>A stack entry as the wizard records it: a catalogue product or a free name, with its line if one is given.</summary>
    internal static void Describe(Component c, string product, string? line)
    {
        if (product.StartsWith("name:", StringComparison.Ordinal))
        {
            var name = product["name:".Length..];
            var slash = name.IndexOf('/', StringComparison.Ordinal);
            (c.CustomVendor, c.CustomName) = slash < 0 ? (null, name) : (name[..slash], name[(slash + 1)..]);
        }
        else
        {
            c.ProductKey = product;
        }

        if (line is not null)
        {
            (c.Version, c.VersionPrecision) = (line, "line");
        }
    }

    /// <summary>Every CVE record of the week, newest state each: a later day's archive wins.</summary>
    private static List<RawRecord> WeekRecords(string folder)
    {
        var latest = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var day in Directory.GetDirectories(folder).Where(d => Path.GetFileName(d).All(char.IsAsciiDigit)).Order(StringComparer.Ordinal))
        {
            foreach (var path in Directory.EnumerateFiles(day, "CVE-*.json", SearchOption.AllDirectories))
            {
                latest[Path.GetFileNameWithoutExtension(path)] = path;
            }
        }

        Assert.True(latest.Count > 0, $"No CVE-*.json below {folder}.");
        return [.. latest.Select(r =>
        {
            using var record = JsonDocument.Parse(File.ReadAllText(r.Value));
            return new RawRecord(r.Key, JsonSerializer.Serialize(record.RootElement), "application/json");
        })];
    }

    /// <summary>The notices the generation wrote for a stack: distinct entries.</summary>
    private static HashSet<string> Generated(IReadOnlyList<Raised> notices, Stack stack, StackEntry? entry = null, bool newOnly = false) =>
        [.. notices.Where(n => n.Stack == stack && (entry is null || n.Entry == entry) && (!newOnly || n.IsNew)).Select(n => n.Cve)];

    /// <summary>What ADR 044 counted from the hits: a hit on the product itself whose entry meets the threshold.</summary>
    private static HashSet<string> Counted(IReadOnlyList<Hit> hits, Stack stack, Func<StackEntry, Threshold> threshold, bool newOnly = false) =>
        [.. hits.Where(h => h.Stack == stack && h.Counts && h.Meets(threshold(h.Entry)) && (!newOnly || h.IsNew)).Select(h => h.Cve)];

    private static string Report(int records, IReadOnlyList<string> entries, IReadOnlyList<Hit> hits, IReadOnlyList<Raised> notices, TimeSpan elapsed)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"# Notice spike, 11 to 17 November 2026, real generation");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{records} records, entries by state: {string.Join(", ", entries.Select(e => e.Replace('\t', ' ')))}; {elapsed.TotalSeconds:F0} s.");
        text.AppendLine();
        text.AppendLine("Counts are distinct entries (CVEs). *Notices*: rows of `assess.notice` naming the stack entry, as the assessment wrote them "
            + "with the entry's threshold «only important» and the catalogue's noisy products. The other columns count hits from the findings: "
            + "*new*: published in the week; *unrated*: no CVSS score; *weak*: hits a broad entry does not cover, never a notice; *line*: hits "
            + "the version line removed; every threshold lets an actively exploited entry pass. *ADR 044*: the harness's count with its thresholds.");
        foreach (var stack in Stacks)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"## {stack.Name}");
            text.AppendLine();
            text.AppendLine("| Entry | All | new | ≥ medium | ≥ high | critical | exploited | unrated | weak | line | ADR 044 | **Notices** |");
            text.AppendLine("|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|");
            foreach (var entry in stack.Entries)
            {
                var own = hits.Where(h => h.Stack == stack && h.Entry == entry).ToList();
                var counted = own.Where(h => h.Counts).ToList();
                int Count(IEnumerable<Hit> some) => some.Select(h => h.Cve).Distinct(StringComparer.Ordinal).Count();
                int Passing(Threshold threshold) => Count(counted.Where(h => h.Meets(threshold)));
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"| {entry.Label} | {Count(counted)} | {Count(counted.Where(h => h.IsNew))} | {Passing(Threshold.Medium)} | {Passing(Threshold.High)} "
                    + $"| {Passing(Threshold.Critical)} | {Passing(Threshold.Exploited)} | {Count(counted.Where(h => h.Score is null))} "
                    + $"| {Count(own.Where(h => !h.Strong).ExceptBy(counted.Select(c => c.Cve), h => h.Cve))} | {Count(own.Where(h => h.Strong && !h.InLine))} "
                    + $"| {Passing(entry.Default)} | **{Generated(notices, stack, entry).Count}** |");
            }

            int Stacked(Threshold threshold) => Counted(hits, stack, _ => threshold).Count;
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| **Stack** | {Stacked(Threshold.Everything)} | {Counted(hits, stack, _ => Threshold.Everything, newOnly: true).Count} | {Stacked(Threshold.Medium)} "
                + $"| {Stacked(Threshold.High)} | {Stacked(Threshold.Critical)} | {Stacked(Threshold.Exploited)} | | | "
                + $"| {Counted(hits, stack, e => e.Default).Count} | **{Generated(notices, stack).Count}** ({Generated(notices, stack, newOnly: true).Count} new) |");
            text.AppendLine();
            var raised = notices.Where(n => n.Stack == stack).GroupBy(n => n.Cve).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g =>
                {
                    var hit = hits.FirstOrDefault(h => h.Stack == stack && h.Cve == g.Key);
                    return $"{g.Key} ({string.Join("/", g.Select(n => n.Entry.Label).Distinct(StringComparer.Ordinal))}, "
                        + $"{hit?.Score?.ToString("F1", CultureInfo.InvariantCulture) ?? "unrated"}{(hit?.Exploited == true ? ", exploited" : string.Empty)}{(g.First().IsNew ? ", new" : string.Empty)})";
                });
            text.AppendLine(CultureInfo.InvariantCulture, $"Notices: {string.Join("; ", raised)}");
        }

        return text.ToString();
    }

    /// <summary>One component a notice names.</summary>
    private sealed record Raised(Stack Stack, StackEntry Entry, string Cve, string State, bool Backfill, bool IsNew)
    {
        public static Raised Parse(string row, IReadOnlyDictionary<Guid, (Stack Stack, StackEntry Entry)> components)
        {
            var c = row.Split('\t');
            var (stack, entry) = components[Guid.Parse(c[1])];
            return new Raised(stack, entry, c[0], c[2], bool.Parse(c[3]), c[4].Length > 0 && DateTimeOffset.Parse(c[4], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) >= WeekStart);
        }
    }

    /// <summary>One finding: an entry's hit on one component of a stack.</summary>
    private sealed record Hit(Stack Stack, StackEntry Entry, string Cve, bool IsNew, double? Score, bool Exploited, bool Strong, bool InLine)
    {
        /// <summary>A hit a broad entry covers (master plan 17.2, 17.3): on the product itself, within the line.</summary>
        public bool Counts => Strong && InLine;

        public bool Meets(Threshold threshold) => threshold switch
        {
            Threshold.Everything => true,
            Threshold.Medium => Exploited || Score >= 4.0,
            Threshold.High => Exploited || Score >= 7.0,
            Threshold.Critical => Exploited || Score >= 9.0,
            _ => Exploited,
        };

        public static Hit Parse(string row, IReadOnlyDictionary<Guid, (Stack Stack, StackEntry Entry)> components)
        {
            var c = row.Split('\t');
            var (stack, entry) = components[Guid.Parse(c[0])];
            var published = c[2].Length == 0 ? (DateTimeOffset?)null : DateTimeOffset.Parse(c[2], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            var strong = c[5] is "product" or "cpe" or "package";
            using var evidence = JsonDocument.Parse(c[6]);
            var statements = evidence.RootElement.TryGetProperty("version", out var version) && version.TryGetProperty("statements", out var list)
                ? list.Deserialize<List<VersionStatement>>(Json) ?? []
                : [];
            return new Hit(
                stack, entry, c[1], published >= WeekStart, c[3].Length == 0 ? null : double.Parse(c[3], CultureInfo.InvariantCulture), c[4] == "active",
                strong, entry.Line is null || !strong || LineNarrowing.Keeps(statements, VersionLines.LeadingNumbers(entry.Line)));
        }
    }
}
