using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Sources.Abstractions;
using Ardea.TestSupport;

namespace Ardea.Assessment.Tests.Reference;

/// <summary>
/// The notice spike of task 5a.1 (master plan 17.9, ADR 044): how many notices a typical organisation stack gets in
/// one week, per stack entry and threshold. Every record of the spike week (11 to 17 November 2026, newest state
/// each) goes through the real pipeline (CVE Program parser, correlation, forward search, candidates, version check)
/// against one organisation per stack, whose components carry no version. A notice is counted where the entry hits
/// the product itself (match type product, CPE or the product's own package; master plan 17.2), after an optional
/// version line narrowed it (17.3), once per organisation and entry.
/// <para>
/// Opt-in, never in CI (master plan 13.6): the full week is not in the repository. Unpack the CVE Program's delta
/// archives of the week into one folder per day (<c>11</c> … <c>17</c>) and run
/// <c>ARDEA_SPIKE_DIR=&lt;folder&gt; dotnet test --project tests/Ardea.Assessment.Tests --filter-class "*NoticeSpike"</c>.
/// The report lands next to the test binaries as <c>notice-spike-report.md</c>.
/// </para>
/// </summary>
public sealed class NoticeSpike(PostgresServer server)
{
    /// <summary>The acceptance of phase 5a (ADR 044): a stack of ten entries gets at most this many notices in the spike week.</summary>
    internal const int MaxNoticesPerWeek = 25;

    /// <summary>Records published from this day on are new in the week; older ones changed in it (the 10th was a Patch Tuesday).</summary>
    private static readonly DateTimeOffset WeekStart = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Thresholds of a stack entry; each lets an actively exploited entry pass.</summary>
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
    /// <param name="Line">A version line such as <c>17</c> or <c>8.3</c>, or <c>null</c> for none.</param>
    /// <param name="Default">The entry's «only important» as ADR 044 decides it.</param>
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
            var project = await host.AddProjectAsync(organization, stack.Name, new ReferenceProject("normal", "internet"));
            foreach (var entry in stack.Entries)
            {
                foreach (var product in entry.Products)
                {
                    var id = await host.AddComponentAsync(organization, project, c =>
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
                    });
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
        var entries = await host.RowsAsync("SELECT state, count(*) FROM intel.security_entry GROUP BY state ORDER BY state");

        var report = Report(records.Count, entries, hits, watch.Elapsed);
        var output = Path.Combine(AppContext.BaseDirectory, "notice-spike-report.md");
        await File.WriteAllTextAsync(output, report, TestContext.Current.CancellationToken);
        TestContext.Current.SendDiagnosticMessage($"Notice spike report: {output}");
        TestContext.Current.SendDiagnosticMessage(report);

        foreach (var stack in Stacks.Where(s => s.Entries.Length == 10))
        {
            var notices = Notices(hits, stack, e => e.Default);
            Assert.True(notices.Count <= MaxNoticesPerWeek, $"{stack.Name}: {notices.Count} notices, at most {MaxNoticesPerWeek} (ADR 044).{Environment.NewLine}{report}");
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

    /// <summary>The entries that raise a notice in the stack: a hit on the product itself whose entry meets the threshold.</summary>
    private static HashSet<string> Notices(IReadOnlyList<Hit> hits, Stack stack, Func<StackEntry, Threshold> threshold, bool newOnly = false) =>
        [.. hits.Where(h => h.Stack == stack && h.Counts && h.Meets(threshold(h.Entry)) && (!newOnly || h.IsNew)).Select(h => h.Cve)];

    private static string Report(int records, IReadOnlyList<string> entries, IReadOnlyList<Hit> hits, TimeSpan elapsed)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"# Notice spike, 11 to 17 November 2026");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{records} records, entries by state: {string.Join(", ", entries.Select(e => e.Replace('\t', ' ')))}; {elapsed.TotalSeconds:F0} s.");
        text.AppendLine();
        text.AppendLine("Counts are distinct entries (CVEs). Every threshold lets an actively exploited entry pass. *new*: published in the week; "
            + "the others were published before and changed in it. *unrated*: no CVSS score. *weak*: hits a broad entry does not cover (plugins of a "
            + "platform, related products, free names), not counted. *line*: hits the version line removed. *Default*: the entry's threshold per ADR 044.");
        foreach (var stack in Stacks)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"## {stack.Name}");
            text.AppendLine();
            text.AppendLine("| Entry | All | new | ≥ medium | ≥ high | critical | exploited | unrated | weak | line | Default | Notices |");
            text.AppendLine("|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|---|--:|");
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
                    + $"| {entry.Default} | {Passing(entry.Default)} |");
            }

            int Stacked(Threshold threshold) => Notices(hits, stack, _ => threshold).Count;
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| **Stack** | **{Stacked(Threshold.Everything)}** | {Notices(hits, stack, _ => Threshold.Everything, newOnly: true).Count} | {Stacked(Threshold.Medium)} "
                + $"| **{Stacked(Threshold.High)}** | **{Stacked(Threshold.Critical)}** | {Stacked(Threshold.Exploited)} | | | | "
                + $"| **{Notices(hits, stack, e => e.Default).Count}** ({Notices(hits, stack, e => e.Default, newOnly: true).Count} new) |");
            text.AppendLine();
            var raised = hits.Where(h => h.Stack == stack && h.Counts && h.Meets(h.Entry.Default))
                .GroupBy(h => h.Cve).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key} ({string.Join("/", g.Select(h => h.Entry.Label).Distinct(StringComparer.Ordinal))}, {g.First().Score?.ToString("F1", CultureInfo.InvariantCulture) ?? "unrated"}{(g.First().Exploited ? ", exploited" : string.Empty)}{(g.First().IsNew ? ", new" : string.Empty)})");
            text.AppendLine(CultureInfo.InvariantCulture, $"Notices with the default thresholds: {string.Join("; ", raised)}");
        }

        return text.ToString();
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
            return new Hit(
                stack, entry, c[1], published >= WeekStart, c[3].Length == 0 ? null : double.Parse(c[3], CultureInfo.InvariantCulture), c[4] == "active",
                strong, entry.Line is null || !strong || LineNarrowing.Keeps(evidence.RootElement, VersionLines.LeadingNumbers(entry.Line)));
        }
    }
}

public sealed class LineNarrowingTests
{
    [Theory]
    [InlineData("""{"introduced":"17.0","fixed":"17.7","status":"affected"}""", "17", true)]
    [InlineData("""{"introduced":"16","fixed":"16.11","status":"affected"}""", "17", false)]
    [InlineData("""{"introduced":"0","fixed":"16.11","status":"affected"}""", "17", false)]
    [InlineData("""{"introduced":"8.3.0","fixed":"8.3.27","status":"affected"}""", "8.3", true)]
    [InlineData("""{"introduced":"8.3.0","fixed":"8.3.27","status":"affected"}""", "8.4", false)]
    [InlineData("""{"introduced":"0","fixed":"17.0","status":"affected"}""", "17", false)]
    [InlineData("""{"lastAffected":"16.4","status":"affected"}""", "17", false)]
    [InlineData("""{"introduced":"18.0","status":"affected"}""", "17", false)]
    [InlineData("""{"introduced":"15.0","status":"affected"}""", "17", true)]
    [InlineData("""{"introduced":"0","fixed":"*","status":"affected"}""", "17", true)]
    [InlineData("""{"exact":"17","status":"affected"}""", "17", true)]
    [InlineData("""{"exact":"9.6","status":"affected"}""", "17", false)]
    [InlineData("""{"exact":"0","status":"affected"}""", "17", null)]
    public void A_range_touches_a_line_when_its_bounds_overlap_it(string range, string line, bool? touches)
    {
        using var json = JsonDocument.Parse(range);
        Assert.Equal(touches, LineNarrowing.Touches(json.RootElement, VersionLines.LeadingNumbers(line)));
    }

    [Fact]
    public void An_entry_stays_unless_every_affected_range_lies_outside_the_line()
    {
        static bool Keeps(string ranges)
        {
            using var json = JsonDocument.Parse($$$"""{"version":{"statements":[{"ranges":{{{ranges}}}}]}}""");
            return LineNarrowing.Keeps(json.RootElement, [17]);
        }

        Assert.False(Keeps("""[{"introduced":"16","fixed":"16.11","status":"affected"},{"introduced":"15","fixed":"15.15","status":"affected"}]"""));
        Assert.True(Keeps("""[{"introduced":"16","fixed":"16.11","status":"affected"},{"introduced":"17","fixed":"17.7","status":"affected"}]"""));
        Assert.True(Keeps("""[{"introduced":"16","fixed":"16.11","status":"affected"},{"exact":"n/a","status":"affected"}]"""));
        Assert.True(Keeps("""[{"introduced":"17.7","status":"unaffected"}]"""));
        Assert.True(Keeps("[]"));
    }
}

/// <summary>
/// The narrowing by a version line (master plan 17.3) as this spike assumes it: an entry stays unless every affected
/// range it states for the product lies outside the line. A range the line numbers cannot place, or an entry without
/// ranges, stays: a line narrows only what the source rules out.
/// </summary>
internal static class LineNarrowing
{
    public static bool Keeps(JsonElement evidence, long[] line)
    {
        if (!evidence.TryGetProperty("version", out var version) || !version.TryGetProperty("statements", out var statements))
        {
            return true;
        }

        var ranges = statements.EnumerateArray()
            .SelectMany(s => s.TryGetProperty("ranges", out var r) ? r.EnumerateArray() : Enumerable.Empty<JsonElement>())
            .Where(r => Text(r, "status") == "affected")
            .ToList();
        return ranges.Count == 0 || ranges.Exists(r => Touches(r, line) != false);
    }

    /// <summary>Whether a range overlaps the line; <c>null</c> when its bounds do not say.</summary>
    internal static bool? Touches(JsonElement range, long[] line)
    {
        var start = line;
        long[] end = [.. line[..^1], line[^1] + 1];
        if (Text(range, "exact") is { } exact)
        {
            return exact == "0" || VersionLines.LeadingNumbers(exact).Length == 0 ? null : VersionLines.InLine(exact, line);
        }

        var introduced = Text(range, "introduced");
        var upper = Text(range, "fixed");
        var last = Text(range, "lastAffected");
        if (introduced is null && upper is null && last is null)
        {
            return null;
        }

        var lowerOk = VersionLines.CompareNumbers(VersionLines.LeadingNumbers(introduced), end) < 0;
        var upperOk = upper is not null && VersionLines.LeadingNumbers(upper) is { Length: > 0 } fixedAt ? VersionLines.CompareNumbers(fixedAt, start) > 0
            : last is not null && VersionLines.LeadingNumbers(last) is { Length: > 0 } lastAt ? VersionLines.CompareNumbers(lastAt, start) >= 0
            : true;
        return lowerOk && upperOk;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
